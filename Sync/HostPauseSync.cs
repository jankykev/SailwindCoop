using System;
using System.Reflection;
using SailwindCoop.Net;
using SailwindCoop.Runtime;
using UnityEngine;

namespace SailwindCoop.Sync
{
    /// <summary>
    /// Mirrors a host gameplay pause on the ready client. The host also pauses during save transfer,
    /// but the client must keep running until its streamed world has finished loading.
    ///
    /// During a gameplay pause, freezing the client's simulation prevents loose items and local
    /// physics from advancing beyond the authoritative host world. The game's settings menu stores
    /// its previous time scale in <c>unpausedTimescale</c>; that value is repaired when necessary so
    /// closing a menu opened during the network pause cannot leave the client frozen at zero.
    ///
    /// The client resumes when the host unpauses or the connection ends. A stale pause state must
    /// never leave the client permanently frozen.
    /// </summary>
    public sealed class HostPauseSync
    {
        private static readonly FieldInfo UnpausedTimescaleField =
            typeof(StartMenu).GetField("unpausedTimescale", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly CoopNet _net;
        private bool _frozen;
        private bool _ownsTimeScale;
        private float _resumeTimeScale = 1f;
        private CoopLog.Repeat _controlReport;

        public HostPauseSync(CoopNet net) { _net = net; }

        /// <summary>Whether the client is currently frozen by the host's pause.</summary>
        public bool Frozen => _frozen;

        public void Tick(EnvironmentSync env)
        {
            // JoinPause deliberately stops the host while the client receives and loads the save.
            // Freezing the client in that window can suspend Unity's load/finalization work and crash.
            // Only mirror ordinary gameplay pauses after this client has reported Ready.
            var coop = CoopBehaviour.Instance;
            bool want = _net.Role == Role.Client &&
                        _net.State == LinkState.Connected &&
                        coop != null && coop.ClientJoined &&
                        env != null && env.HasHostState &&
                        env.HostTimeScale <= 0.0001f;
            SetFrozen(want);
        }

        public void Clear() { SetFrozen(false); }

        private void SetFrozen(bool value)
        {
            if (value == _frozen) return;
            _frozen = value;
            try
            {
                if (value)
                {
                    // Stop the client's world simulation as well as its player controller. Otherwise
                    // loose items and local physics continue while the host's authoritative world is
                    // frozen. Preserve an already-open game's menu restore value if it paused first.
                    float savedMenuScale = ReadMenuTimescale();
                    _resumeTimeScale = Time.timeScale > 0.0001f ? Time.timeScale :
                        (savedMenuScale > 0.0001f ? savedMenuScale : 1f);
                    _ownsTimeScale = Time.timeScale > 0.0001f;
                    if (_ownsTimeScale) Time.timeScale = 0f;
                    Refs.SetPlayerControl(false);
                }
                else
                {
                    if (IsGameMenuOpen())
                    {
                        // The vanilla menu restores the value it captured on opening. Replace a
                        // captured zero with the pre-host-pause scale so closing it cannot softlock.
                        WriteMenuTimescale(_resumeTimeScale);
                    }
                    else if ((_ownsTimeScale || Time.timeScale <= 0.0001f) && Time.timeScale <= 0.0001f)
                    {
                        Time.timeScale = _resumeTimeScale > 0.0001f ? _resumeTimeScale : 1f;
                    }
                    _ownsTimeScale = false;

                    if (!IsGameMenuOpen() && (SleepSync.Instance == null || !SleepSync.Instance.ClientAsleep) && GameState.inBed == null)
                    {
                        // SleepSync owns player control while the client is asleep; leave it disabled.
                        Refs.SetPlayerControl(true);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Logger.ReportError("[HostPauseSync] Could not toggle player control",
                                          e.Message, ref _controlReport);
            }
            Plugin.Logger.LogInfo("[HostPauseSync] client " + (value ? "frozen (host paused)" : "released"));
        }

        private static bool IsGameMenuOpen()
        {
            try { return GameState.inCursorMenu; }
            catch { return false; }
        }

        private static float ReadMenuTimescale()
        {
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<StartMenu>();
                return menu != null && UnpausedTimescaleField != null
                    ? (float)UnpausedTimescaleField.GetValue(menu) : 0f;
            }
            catch { return 0f; }
        }

        private static void WriteMenuTimescale(float value)
        {
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<StartMenu>();
                if (menu != null && UnpausedTimescaleField != null)
                    UnpausedTimescaleField.SetValue(menu, value > 0.0001f ? value : 1f);
            }
            catch { /* Menu recovery is best-effort; never interrupt network pause release. */ }
        }
    }
}
