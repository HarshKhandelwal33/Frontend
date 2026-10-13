using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [InitializeOnLoad]
    public static class GearboxMotionChecks
    {
        static GearboxMotionChecks() {
            EditorApplication.delayCall+=()=> {
                const string marker=".simulation-check/check-motion";
                if(File.Exists(marker)) { File.Delete(marker);Run(); }
            };
        }
        [MenuItem("Tools/Gearbox Demo/Verify Joint Motion Profiles")]
        public static void Run() {
            Directory.CreateDirectory(".simulation-check");
            string report=".simulation-check/motion-profile-report.txt";
            try {
                var random=new System.Random(42);
                for(int trial=0;trial<100;trial++) {
                    var from=new float[6];var to=new float[6];
                    for(int j=0;j<6;j++) { from[j]=(float)random.NextDouble()*300-150;to[j]=(float)random.NextDouble()*300-150; }
                    float duration=RobotMotionProfile.Duration(from,to,35,80);
                    for(int sample=0;sample<=1000;sample++) {
                        float t=sample/1000f;
                        float velocity=30*t*t*(t-1)*(t-1)/duration;
                        float acceleration=60*t*(2*t*t-3*t+1)/(duration*duration);
                        for(int j=0;j<6;j++) {
                            float distance=Mathf.Abs(to[j]-from[j]);
                            if(distance*velocity>35.001f || distance*Mathf.Abs(acceleration)>80.001f)
                                throw new Exception("Joint speed/acceleration bound exceeded.");
                        }
                    }
                }
                if(RobotMotionProfile.Blend(0)!=0 || RobotMotionProfile.Blend(1)!=1)throw new Exception("Profile endpoints invalid.");
                bool rejected=false;
                try { RobotMotionProfile.Duration(new float[6],new[]{float.NaN,0,0,0,0,0},35,80); }
                catch(ArgumentException) { rejected=true; }
                if(!rejected)throw new Exception("Non-finite trajectory accepted.");
                File.WriteAllText(report,"PASS 100 six-joint profiles, 1001 time samples each: velocity <=35 deg/s, acceleration <=80 deg/s².\nPASS endpoints and non-finite trajectory rejection.\n");
                Debug.Log("Joint motion profile checks passed.");
            } catch(Exception e) { File.WriteAllText(report,"FAIL "+e);Debug.LogException(e); }
        }
    }
}
