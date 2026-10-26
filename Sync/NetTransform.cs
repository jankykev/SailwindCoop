using System.Collections.Generic;
using UnityEngine;

namespace SailwindCoop.Sync
{
    /// <summary>
    /// F4 — buffered interpolation/extrapolation for one remote transform.
    ///
    /// Snapshots arrive over Unreliable with a host tick; we don't apply them raw
    /// (that would jitter with every late/lost/jittered packet). Instead we buffer
    /// them and render the remote object at <c>(now - interpDelay)</c>, interpolating
    /// between the two snapshots that bracket that time. If we've run past the newest
    /// snapshot (packet loss), we extrapolate briefly from the last known velocity.
    ///
    /// Snapshots are stored in REAL (origin-stable) space; conversion to local space
    /// happens at apply time every frame, because the floating origin keeps drifting.
    /// </summary>
    public sealed class NetTransform
    {
        private struct Snap
        {
            public long Tick;        // host tick (ms)
            public Vector3 RealPos;  // origin-stable position
            public Quaternion Rot;
            public Vector3 RealVel;  // real-space velocity for extrapolation (may be zero)
        }

        private readonly SnapshotBuffer<Snap> _buf = new SnapshotBuffer<Snap>(s => s.Tick, 32);
        private const float MaxExtrapolateMs = 250f;

        /// <summary>Interpolation delay in ms (config). Larger = smoother but more lag.</summary>
        public float InterpDelayMs = 100f;

        public bool HasData => _buf.Count > 0;

        /// <summary>Buffered snapshot count. A consumer that must not act on a single (possibly
        /// stale/first) sample can wait for two before trusting the stream.</summary>
        public int SampleCount => _buf.Count;

        /// <summary>
        /// Converts a buffered position/rotation into world space at apply time. Swappable
        /// so a remote can be expressed in real (origin-stable) space OR boat-local space —
        /// the latter places same-ship players correctly regardless of floating origin.
        /// Defaults to real-space (FloatingOriginManager) conversion.
        /// </summary>
        public System.Func<Vector3, Vector3> ToWorldPos = CoordSpace.RealToLocal;
        public System.Func<Quaternion, Quaternion> ToWorldRot = q => q;

        /// <summary>Feed a snapshot (real space). Out-of-order packets are inserted in order.</summary>
        public void Push(long tick, Vector3 realPos, Quaternion rot, Vector3 realVel = default)
        {
            var s = new Snap { Tick = tick, RealPos = realPos, Rot = rot, RealVel = realVel };

            _buf.Push(s);
        }

        /// <summary>
        /// Sample the buffer at host time <paramref name="serverTickNow"/> and write the
        /// resulting LOCAL-space pose into <paramref name="t"/>. No-op until data exists.
        /// </summary>
        public void Apply(Transform t, long serverTickNow)
        {
            if (t == null) return;
            if (!Sample(serverTickNow, out Vector3 pos, out Quaternion rot, out _)) return;
            t.position = pos;
            t.rotation = rot;
        }

        /// <summary>
        /// The pose <see cref="Apply"/> would write, for a caller that places something else from it
        /// (a boat's body from its hull's pose). <paramref name="realVel"/> is the sampled velocity;
        /// the origin only translates, so it is the same in local space. False until data exists.
        /// </summary>
        public bool Sample(long serverTickNow, out Vector3 pos, out Quaternion rot, out Vector3 realVel)
        {
            pos = Vector3.zero; rot = Quaternion.identity; realVel = Vector3.zero;
            if (_buf.Count == 0) return false;

            long renderTick = serverTickNow - (long)InterpDelayMs;

            // Before the oldest sample: snap to oldest.
            if (renderTick <= _buf[0].Tick)
            {
                Convert(_buf[0].RealPos, _buf[0].Rot, out pos, out rot);
                realVel = _buf[0].RealVel;
                return true;
            }

            // After the newest sample: extrapolate from it (bounded).
            var last = _buf[_buf.Count - 1];
            if (renderTick >= last.Tick)
            {
                float ahead = Mathf.Min(renderTick - last.Tick, MaxExtrapolateMs) / 1000f;
                Convert(last.RealPos + last.RealVel * ahead, last.Rot, out pos, out rot);
                realVel = last.RealVel;
                return true;
            }

            // Find bracketing pair and lerp.
            for (int i = 0; i < _buf.Count - 1; i++)
            {
                var a = _buf[i];
                var b = _buf[i + 1];
                if (renderTick >= a.Tick && renderTick <= b.Tick)
                {
                    float span = b.Tick - a.Tick;
                    float f = span > 0 ? (renderTick - a.Tick) / span : 0f;
                    Vector3 position = InterpolatePosition(a, b, f, span * 0.001f);
                    Convert(position, Quaternion.Slerp(a.Rot, b.Rot, f),
                            out pos, out rot);
                    realVel = Vector3.Lerp(a.RealVel, b.RealVel, f);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Hermite interpolation uses the host's sampled velocity as the tangent at
        /// each end of the segment. This avoids the small stop/start in linear interpolation
        /// whenever a new network snapshot becomes the interpolation bracket.</summary>
        private static Vector3 InterpolatePosition(Snap a, Snap b, float t, float seconds)
        {
            if (seconds <= 0f) return b.RealPos;
            Vector3 delta = b.RealPos - a.RealPos;
            float distance = delta.magnitude;
            if (distance < 0.0001f) return Vector3.Lerp(a.RealPos, b.RealPos, t);

            // Keep noisy packet-derived tangents from bending the boat path away from the
            // segment. RealVel is measured by finite differences and should normally be close
            // to the segment velocity; a 2x cap leaves room for real acceleration.
            Vector3 secant = delta / seconds;
            float maxTangent = secant.magnitude * 2f;
            Vector3 v0 = Vector3.ClampMagnitude(a.RealVel, maxTangent) * seconds;
            Vector3 v1 = Vector3.ClampMagnitude(b.RealVel, maxTangent) * seconds;

            float t2 = t * t, t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            Vector3 result = h00 * a.RealPos + h10 * v0 + h01 * b.RealPos + h11 * v1;

            // Bound modest overshoot while preserving the smooth tangent through normal turns.
            Vector3 padding = Vector3.one * Mathf.Max(0.05f, distance * 0.1f);
            result.x = Mathf.Clamp(result.x, Mathf.Min(a.RealPos.x, b.RealPos.x) - padding.x,
                                   Mathf.Max(a.RealPos.x, b.RealPos.x) + padding.x);
            result.y = Mathf.Clamp(result.y, Mathf.Min(a.RealPos.y, b.RealPos.y) - padding.y,
                                   Mathf.Max(a.RealPos.y, b.RealPos.y) + padding.y);
            result.z = Mathf.Clamp(result.z, Mathf.Min(a.RealPos.z, b.RealPos.z) - padding.z,
                                   Mathf.Max(a.RealPos.z, b.RealPos.z) + padding.z);
            return result;
        }

        private void Convert(Vector3 storedPos, Quaternion storedRot, out Vector3 pos, out Quaternion rot)
        {
            pos = ToWorldPos != null ? ToWorldPos(storedPos) : storedPos;
            rot = ToWorldRot != null ? ToWorldRot(storedRot) : storedRot;
        }

        public void Clear() => _buf.Clear();
    }
}
