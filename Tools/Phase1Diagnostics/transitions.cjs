// Execute the production crate-request branch against a tiny registry model.
// These checks do not substitute for a Unity host/client reproduction.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const cp = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'Sync/ItemSync.cs'), 'utf8');
function block(marker) {
    const start = source.indexOf(marker);
    assert(start >= 0, marker);
    const brace = source.indexOf('{', start);
    let end = brace + 1, depth = 1;
    for (; depth; end++) {
        assert(end < source.length, 'Unclosed block');
        if (source[end] === '{') depth++;
        if (source[end] === '}') depth--;
    }
    return source.slice(brace, end);
}
const crate = block('if (msg.Action == ItemAction.Crate)');
const free = block('private static void EnterFreeDynamic(');
const diagnostics = block('private void TickTransitionDiagnostics(');
const hostDrop = source.slice(source.indexOf('else if (_net.Role == Role.Host)', source.indexOf('public void NotifyDrop')),
    source.indexOf('public void OnItemRequest'));
assert.match(hostDrop, /if \(state.CrateId == 0\)\s+EnterFreeDynamic\(item, state.Frame, Vector3.zero,/);
assert(hostDrop.indexOf('if (placedAttach)') < hostDrop.indexOf('EnterFreeDynamic('));
const config = fs.readFileSync(path.join(root, 'Plugin.cs'), 'utf8');
for (const name of ['RepairClientCrateRequests', 'RestoreHostDropPhysics'])
    assert(!config.includes(`c.Bind("Physics", "${name}",`));
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'sailwind-physics-transitions-'));
fs.writeFileSync(path.join(dir, 'Test.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net46</TargetFramework><LangVersion>9.0</LangVersion></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="all"/></ItemGroup></Project>`);
fs.writeFileSync(path.join(dir, 'Program.cs'), `
using System;
using System.Collections.Generic;
namespace LiteNetLib { enum DeliveryMethod { ReliableOrdered } }
enum CoordFrame { World, Boat }
struct Vector3 { public static Vector3 zero = new Vector3(); }
class ShipItem {
    public int crate; public object held = new object();
    public bool stove, driver, rootCol, visible, layer, dynamic, world, boatAxes;
}
class Entry { public ShipItem Item = new ShipItem(); public uint HolderNetId = 2; public int InventorySlot = 1, Index = 18; }
class Msg { public int InstanceId = 18, PrefabIndex = 1, CrateId, RequestId = 131; }
static class Plugin {
    public static Config Cfg = new Config(); public static Log Logger = new Log();
    public class Config { }
    public class Log { public bool Enabled = false; }
}
static class Time { public static float unscaledTime = 0; }
class Clock { public long ServerTick = 0; }
class Net {
    public Clock Clock = new Clock(); public int broadcasts;
    public void Broadcast(object state, LiteNetLib.DeliveryMethod method) { broadcasts++; }
}
static class ItemComponents { public static bool InStove(ShipItem item) => item.stove; }
class Test {
    Entry entry = new Entry();
    Net _net = new Net();
    Dictionary<ShipItem, uint> _localHeld = new Dictionary<ShipItem, uint>();
    Dictionary<ShipItem, float> _transitionDiagnostics = new Dictionary<ShipItem, float>();
    Entry HostLookup(int id, int prefab) => entry;
    int CrateIdOf(ShipItem item) => item.crate;
    object CrateInventoryFor(int id) => id == 1138143135 || id == 42 ? this : null;
    string TryApplyCrateMembership(ShipItem item, int id) { item.crate = id; return "Applied"; }
    object BuildState(Entry e, long tick) => e.Item;
    static int logs;
    static void LogItemTransition(string label, ShipItem item) { logs++; }
    void Remember(string message) { }
    void Request(Msg msg) { uint actor = 2; ${crate} }
    static void EnsureWorldParentState(ShipItem item) { item.world = true; }
    static void SetRemoteInventoryVisual(ShipItem item, bool hidden) { item.visible = !hidden; }
    static void RestoreInteractableLayer(ShipItem item) { item.layer = true; }
    static void SetRootCollider(ShipItem item, bool enabled) { item.rootCol = enabled; }
    static void SetPuppet(ShipItem item, bool puppet) { item.driver = !puppet; }
    static Vector3 BoatToProxyAxes(ShipItem item, Vector3 vel) { item.boatAxes = true; return vel; }
    static void MoveProxyToItem(ShipItem item, bool kinematic, Vector3 vel) { item.dynamic = !kinematic; }
    static void EnterFreeDynamic(ShipItem item, CoordFrame frame, Vector3 vel, string reason) ${free}
    void TickTransitionDiagnostics() ${diagnostics}
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Main() {
        var t = new Test();
        t.Request(new Msg { CrateId = 1138143135 });
        Check(t.entry.Item.crate == 1138143135 && t.entry.HolderNetId == 0 && t.entry.Item.held == null,
            "Client insert did not establish host membership/release hand");
        t.Request(new Msg { CrateId = 42 });
        Check(t.entry.Item.crate == 42, "Transfer ignored requested target");
        t.Request(new Msg { CrateId = 0 });
        Check(t.entry.Item.crate == 0, "Withdrawal ignored zero target");
        t = new Test();
        t.Request(new Msg { CrateId = 999 });
        Check(t.entry.Item.crate == 0 && t.entry.HolderNetId == 2 && t.entry.Item.held != null && t._net.broadcasts == 1,
            "Unresolved crate mutated item or did not return authoritative state");
        foreach (var frame in new[] { CoordFrame.World, CoordFrame.Boat }) {
            var item = new ShipItem();
            EnterFreeDynamic(item, frame, Vector3.zero, "host local drop");
            Check(item.driver && item.rootCol && item.visible && item.layer && item.dynamic, "Free drop left puppet state");
            Check(item.world == (frame == CoordFrame.World) && item.boatAxes == (frame == CoordFrame.Boat),
                "Boat frame was detached or velocity axes ignored");
        }
        var stored = new ShipItem { stove = true };
        EnterFreeDynamic(stored, CoordFrame.World, Vector3.zero, "stove");
        Check(!stored.dynamic && !stored.driver, "Free transition activated stove contents");
        Plugin.Logger.Enabled = true;
        t._transitionDiagnostics[t.entry.Item] = 0.5f; logs = 0;
        Time.unscaledTime = 0.49f; t.TickTransitionDiagnostics();
        Check(logs == 0 && t._transitionDiagnostics.Count == 1, "Delayed log emitted too soon");
        Time.unscaledTime = 0.5f; t.TickTransitionDiagnostics(); t.TickTransitionDiagnostics();
        Check(logs == 1 && t._transitionDiagnostics.Count == 0, "Delayed log repeated or missed deadline");
        t._transitionDiagnostics[t.entry.Item] = 1f;
        Plugin.Logger.Enabled = false; t.TickTransitionDiagnostics();
        Check(logs == 1 && t._transitionDiagnostics.Count == 0, "Logging off retained pending diagnostics");
        Console.WriteLine("PASS: insert, transfer, withdrawal, unresolved target, free world/boat physics, stove guard, delayed logging");
    }
}`);
for (const [exe, args] of [
    ['dotnet', ['build', path.join(dir, 'Test.csproj'), '-c', 'Release', '--nologo', '-v:q']],
    [path.join(dir, 'bin/Release/net46/Test.exe'), []]
]) {
    const result = cp.spawnSync(exe, args, { encoding: 'utf8' });
    process.stdout.write(result.stdout || '');
    process.stderr.write(result.stderr || '');
    console.log(`${path.basename(exe)} exit code: ${result.status}`);
    if (result.error) throw result.error;
    if (result.status !== 0) process.exit(result.status || 1);
}
