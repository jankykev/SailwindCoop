using System;
using System.Reflection;
using HarmonyLib;
using SailwindCoop.Net;
using UnityEngine;

namespace SailwindCoop.Sync
{
    /// <summary>
    /// Stage 1 — global environment (host -> client): wind, sun/time-of-day, moon.
    /// These are simple global singletons, so they don't need interpolation; we send
    /// a few snapshots a second and the client writes them straight onto the engine.
    ///
    /// Wind is the one subtlety: <c>Wind.Update</c> drifts <c>currentWind</c> toward its
    /// own random targets and would fight us between snapshots, so on the client we
    /// disable the Wind component and drive its static fields directly (restored on
    /// disconnect). The sun advances smoothly on its own; we just re-anchor its time
    /// each snapshot, which stays imperceptible since the sun moves slowly.
    ///
    /// Weather/storms are deliberately out of scope here — see <see cref="EnvStateMsg"/>.
    /// </summary>
    public sealed class EnvironmentSync
    {
        private readonly CoopNet _net;
        private float _sendStamp = float.NegativeInfinity;
        private bool _windDisabled;     // client: did we turn off the local Wind sim?
        private bool _wavesDisabled;    // client: did we turn off the local WavesInertia sim?
        private WavesInertia _waves;    // cached; no singleton on the engine type
        private Vector3 _targetWind;
        private Vector3 _targetBaseWind;
        private Quaternion _targetWindRotation = Quaternion.identity;
        private bool _haveWindTarget;

        /// <summary>Client wind snapshots arrive at 4 Hz. Smooth their visual/physics input between
        /// packets so sails, yards and rope components do not react to a stepped wind value.</summary>
        private const float WindFollowRate = 8f;

        /// <summary>Часы и форма воды живут в <see cref="CrestWaterSync"/> — это состояние среды,
        /// поэтому едет оно в этом же снапшоте, но вся логика там.</summary>
        public CrestWaterSync Crest;

        /// <summary><c>Time.timeScale</c> хоста из последнего снапшота. Нужен не только воде:
        /// <see cref="SleepSync"/> считает по нему скорость восстановления сил, пока спит хост.</summary>
        public float HostTimeScale { get; private set; } = 1f;
        /// <summary>Пришёл ли хоть один снапшот среды — до него <see cref="HostTimeScale"/> вымышлен.</summary>
        public bool HasHostState { get; private set; }

        /// <summary>Environment snapshot rate (Hz). Slow — env changes gradually.</summary>
        public float EnvHz = 4f;

        public EnvironmentSync(CoopNet net) { _net = net; }

        // -----------------------------------------------------------------
        // Host: capture and broadcast
        // -----------------------------------------------------------------

        public void Tick(float dt)
        {
            if (_net.Role == Role.Client)
            {
                if (Crest != null) Crest.Tick();
                SmoothClientWind(dt);
                return;
            }
            if (_net.Role != Role.Host) return;
            if (_net.State != LinkState.Connected) return;

            // ВАЖНО: интервал по НЕмасштабируемому времени, а не по dt.
            // CoopBehaviour передаёт сюда Time.deltaTime, а он на паузе хоста равен нулю — таймер
            // переставал накапливаться, снапшоты не уходили вовсе, и клиент никогда не узнавал
            // HostTimeScale = 0. Его волновые часы продолжали ехать на последнем известном
            // масштабе 1.0, вода шла при стоящем хосте, а на снятии паузы прилетал скачок фазы.
            // Именно этот снапшот и обязан ходить во время паузы: он её и объявляет.
            float interval = 1f / Mathf.Max(0.5f, EnvHz);
            if (Time.unscaledTime - _sendStamp < interval) return;
            _sendStamp = Time.unscaledTime;

            var msg = new EnvStateMsg
            {
                Tick = _net.Clock.ServerTick,
                Wind = global::Wind.currentWind,
                BaseWind = global::Wind.currentBaseWind,
                WindRot = global::Wind.windRotation,
                Day = GameState.day,
            };

            var sun = Sun.sun;
            if (sun != null)
            {
                msg.GlobalTime = sun.globalTime;
                msg.LocalTime = sun.localTime;
                msg.Timescale = sun.timescale;
            }

            var moon = Moon.instance;
            if (moon != null) msg.MoonPhase = moon.currentPhase;

            var waves = FindWaves();
            if (waves != null)
            {
                msg.HasWaves = true;
                msg.WavesRot = waves.transform.rotation;
                msg.WavesInertia = waves.currentInertia;
                msg.WavesMagnitude = waves.currentMagnitude;
            }

            msg.HostTimeScale = Time.timeScale;

            if (Crest != null) Crest.Capture(msg);

            _net.Broadcast(msg, LiteNetLib.DeliveryMethod.Unreliable);
        }

        private WavesInertia FindWaves()
        {
            if (_waves == null) _waves = UnityEngine.Object.FindObjectOfType<WavesInertia>();
            return _waves;
        }

        // -----------------------------------------------------------------
        // Client: apply
        // -----------------------------------------------------------------

        public void OnEnvState(EnvStateMsg msg, LiteNetLib.NetPeer fromPeer)
        {
            if (_net.Role != Role.Client) return;

            // Wind: take over from the local simulation so it stops fighting us. Keep each
            // snapshot as a target and blend toward it in Tick; writing a new wind every 250 ms
            // makes client sail and rigging physics respond in visible steps.
            var wind = global::Wind.instance;
            if (wind != null && wind.enabled)
            {
                wind.enabled = false;
                _windDisabled = true;
            }
            if (!_haveWindTarget)
            {
                global::Wind.currentWind = msg.Wind;
                global::Wind.currentBaseWind = msg.BaseWind;
                global::Wind.windRotation = msg.WindRot;
                _haveWindTarget = true;
            }
            _targetWind = msg.Wind;
            _targetBaseWind = msg.BaseWind;
            _targetWindRotation = msg.WindRot;

            // Time / sun: re-anchor; the client sun keeps advancing between snapshots.
            var sun = Sun.sun;
            if (sun != null)
            {
                sun.globalTime = msg.GlobalTime;
                sun.localTime = msg.LocalTime;
                sun.timescale = msg.Timescale;
            }
            GameState.day = msg.Day;

            // Moon phase (harmless even if Moon.Update recomputes it from the synced time).
            var moon = Moon.instance;
            if (moon != null) moon.currentPhase = msg.MoonPhase;

            // Waves: same takeover as the wind — WavesInertia.Update drifts toward the local wind
            // and would fight the snapshots. Sea state must match or the host-authoritative boat
            // visibly sinks under (or floats above) the client's water.
            if (msg.HasWaves)
            {
                var waves = FindWaves();
                if (waves != null)
                {
                    if (waves.enabled)
                    {
                        waves.enabled = false;
                        _wavesDisabled = true;
                    }
                    waves.LoadInertia(msg.WavesRot, msg.WavesInertia, msg.WavesMagnitude);
                }
            }

            HostTimeScale = msg.HostTimeScale;
            HasHostState = true;
            if (Crest != null) Crest.ApplyRemote(_net, msg);

        }

        public void Clear()
        {
            // Hand the wind back to the local simulation for single-player.
            if (_windDisabled)
            {
                var wind = global::Wind.instance;
                if (wind != null) wind.enabled = true;
                _windDisabled = false;
            }
            if (_wavesDisabled)
            {
                if (_waves != null) _waves.enabled = true;
                _wavesDisabled = false;
            }
            _waves = null;
            _sendStamp = float.NegativeInfinity;
            _haveWindTarget = false;
            _targetWind = Vector3.zero;
            _targetBaseWind = Vector3.zero;
            _targetWindRotation = Quaternion.identity;

            // Снимок хоста больше не действителен. Без сброса сессия, оборвавшаяся на паузе хоста,
            // оставила бы HostTimeScale = 0 навсегда: следующий клиент был бы заморожен
            // (HostPauseSync) ещё до первого снапшота, а SleepSync считал бы восстановление сил по
            // масштабу чужой, давно закрытой игры.
            HasHostState = false;
            HostTimeScale = 1f;

        }

        private void SmoothClientWind(float dt)
        {
            if (!_haveWindTarget || dt <= 0f) return;
            float blend = 1f - Mathf.Exp(-WindFollowRate * dt);
            global::Wind.currentWind = Vector3.Lerp(global::Wind.currentWind, _targetWind, blend);
            global::Wind.currentBaseWind = Vector3.Lerp(global::Wind.currentBaseWind, _targetBaseWind, blend);
            global::Wind.windRotation = Quaternion.Slerp(global::Wind.windRotation, _targetWindRotation, blend);
        }
    }

}
