// Source-contract check only: Unity runtime reproduction is still required.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
function removeMethod(source, name) {
    const start = source.search(new RegExp(`(?:private|internal) static void ${name}\\(`));
    assert(start >= 0, `Missing ${name}`);
    const brace = source.indexOf('{', start);
    let end = brace + 1, depth = 1;
    for (; depth && end < source.length; end++) {
        if (source[end] === '{') depth++;
        if (source[end] === '}') depth--;
    }
    assert.equal(depth, 0);
    return { source: source.slice(0, start) + source.slice(end), body: source.slice(start, end) };
}
const sync = fs.readFileSync(path.join(root, 'Sync/ItemSync.cs'), 'utf8');
const logger = removeMethod(sync, 'LogItemTransition').body;
const guard = logger.indexOf('!Plugin.Logger.Enabled');
assert(guard >= 0 && guard < logger.indexOf('try'), 'Gate must precede all Unity reads');
assert(logger.indexOf('!Instance.IsSessionActive') < logger.indexOf('try'));
for (const field of ['id', 'holder', 'netSlot', 'beltSlot', 'hand', 'crate', 'listedCrate',
    'uiContains', 'uiButton', 'hanging', 'hook', 'jointBody', 'attached', 'disableCol',
    'inStove', 'layer', 'rootCol', 'irbEnabled', 'rbKin', 'rbDetect', 'jointCount',
    'hookOccupied', 'jointItem', 'rbPos', 'rbSleep']) {
    assert(logger.includes(` ${field}=`), `Missing diagnostic ${field}`);
}
assert(!/\b(?:SetPuppet|MoveProxyToItem|InsertItem|WithdrawItem|RefreshButtons|SetValue)\(/.test(logger),
    'Logger must not repair or mutate game state');
assert(!/\b(?:item|irb|body|ui)\.\w+\s*=(?!=)/.test(logger));

const patches = fs.readFileSync(path.join(root, 'Sync/ItemPatches.cs'), 'utf8');
for (const name of ['PreHangableJoint', 'PostHangableJoint']) {
    const removed = removeMethod(patches, name);
    assert(removed.body.includes('if (Plugin.Logger?.Enabled != true) return;'));
    assert(removed.body.includes('catch (Exception e)'));
}
assert(sync.includes('_transitionDiagnostics.Clear();'), 'Session reset must clear delayed diagnostics');
assert(sync.includes('if (item != null) LogItemTransition("delayed transition +0.5s", item);'));
console.log('PASS: logging gates, required state fields, read-only game diagnostics, delayed sample/reset');
