using System;
using UnityEngine;

namespace GearboxDemo
{
    /// <summary>Synchronized quintic joint motion, in degrees and simulation seconds.</summary>
    public static class RobotMotionProfile
    {
        public static float Blend(float t) {
            t=Mathf.Clamp01(t);
            return t*t*t*(t*(6*t-15)+10);
        }
        public static float Duration(float[] from,float[] to,float velocity,float acceleration,float minimum=0.02f) {
            if(from==null || to==null || from.Length!=6 || to.Length!=6 ||
                velocity<=0 || acceleration<=0 || float.IsNaN(velocity) || float.IsNaN(acceleration) ||
                float.IsInfinity(velocity) || float.IsInfinity(acceleration))throw new ArgumentException("Invalid motion profile.");
            float duration=minimum;
            for(int j=0;j<6;j++) {
                float distance=Mathf.Abs(to[j]-from[j]);
                if(float.IsNaN(distance) || float.IsInfinity(distance))throw new ArgumentException("Non-finite trajectory.");
                duration=Mathf.Max(duration,1.875f*distance/velocity,Mathf.Sqrt(5.774f*distance/acceleration));
            }
            return duration;
        }
    }
}
