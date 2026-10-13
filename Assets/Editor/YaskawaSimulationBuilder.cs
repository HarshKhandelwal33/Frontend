using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GearboxDemo.Editor
{
    public static class YaskawaSimulationBuilder
    {
        public const string ScenePath="Assets/Scenes/GearboxTraining.unity";
        private const string Folder="Assets/Models/YaskawaRobot/";
        private static readonly Vector3[] Pivots={new Vector3(0,.103f,0),new Vector3(0,.245f,-.109f),new Vector3(0,.845f,-.109f),new Vector3(.1405f,.845f,0),new Vector3(.65f,.845f,0),new Vector3(.65f,.7665f,0)};
        private static readonly Vector3[] Axes={Vector3.up,Vector3.back,Vector3.back,Vector3.right,Vector3.back,Vector3.down};

        [MenuItem("Tools/Gearbox Demo/Yaskawa/Use CAD Robot in Training Scene")]
        public static void Build() {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode first.");
            var scene=EditorSceneManager.GetActiveScene();
            if(scene.path!=ScenePath)scene=EditorSceneManager.OpenScene(ScenePath);
            var manager=UnityEngine.Object.FindAnyObjectByType<GearboxAssemblyManager>();
            var arm=manager.handover.robotArm;
            if(arm.GetComponent<YaskawaSimulationRig>()!=null) { Debug.Log("Yaskawa simulation rig is already installed.");return; }
            const string backup="Assets/Scenes/GearboxTraining_PrimitiveBackup.unity";
            if(!File.Exists(backup))EditorSceneManager.SaveScene(scene,backup,true);
            Undo.RegisterFullObjectHierarchyUndo(arm.gameObject,"Replace robot with CAD simulation rig");
            var gripper=manager.handover.robotGripper;
            gripper.transform.SetParent(arm.transform,true);
            foreach(Transform child in arm.transform.Cast<Transform>().ToArray())
                if(child!=gripper.transform)UnityEngine.Object.DestroyImmediate(child.gameObject);
            const float displayScale=1f;
            arm.transform.localScale=Vector3.one*displayScale;
            arm.transform.localRotation=Quaternion.Euler(0,-90,0);
            arm.gameObject.AddComponent<YaskawaSimulationRig>();
            ConfigureCompactCell(manager,arm);
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"YaskawaSimulation.mat");
            if(material==null) {
                material=new Material(Shader.Find("Standard")){color=new Color(.19f,.44f,.68f)};
                AssetDatabase.CreateAsset(material,Folder+"YaskawaSimulation.mat");
            }
            float elevation=0;
            var root=new GameObject("Yaskawa fixed base").transform;root.SetParent(arm.transform,false);
            root.localPosition=Vector3.up*elevation;
            var fixedBody=root.gameObject.AddComponent<ArticulationBody>();fixedBody.immovable=true;fixedBody.mass=30;
            AddVisual(root,"base",Vector3.zero,material);
            Transform parent=root;
            float[] home={0,0,0,0,0,0};
            float[] low={-180,-120,-150,-360,-150,-360};
            float[] high={180,120,150,360,150,360};
            string[] names={"saxis","laxis","uaxis","raxis","baxis","taxis"};
            float[] masses={6,4.5f,3,.9f,.6f,.4f};
            for(int i=0;i<6;i++) {
                var joint=new GameObject("Joint"+(i+1)+" - "+names[i]).transform;
                joint.SetParent(parent,false);joint.localPosition=Pivots[i]-(i==0?Vector3.zero:Pivots[i-1]);
                var body=joint.gameObject.AddComponent<ArticulationBody>();body.jointType=ArticulationJointType.RevoluteJoint;
                body.anchorPosition=Vector3.zero;body.anchorRotation=Quaternion.FromToRotation(Vector3.right,Axes[i]);
                body.matchAnchors=false;body.parentAnchorPosition=joint.localPosition;body.parentAnchorRotation=body.anchorRotation;
                body.twistLock=ArticulationDofLock.LimitedMotion;body.mass=masses[i];body.useGravity=true;
                body.linearDamping=.05f;body.angularDamping=.1f;body.jointFriction=.05f;
                body.maxJointVelocity=1.5f;body.solverIterations=24;body.solverVelocityIterations=12;
                body.xDrive=new ArticulationDrive{lowerLimit=low[i],upperLimit=high[i],stiffness=16000,damping=1800,forceLimit=3500,target=home[i]};
                var controller=joint.gameObject.AddComponent<RobotJointController>();controller.localAxis=Axes[i];arm.joints[i]=controller;
                AddVisual(joint,names[i],Pivots[i],material);
                parent=joint;
            }
            gripper.transform.SetParent(parent,false);
            gripper.transform.localPosition=new Vector3(0,-.08168f,0);
            gripper.transform.localRotation=Quaternion.Euler(180,0,0);
            gripper.transform.localScale=Vector3.one/displayScale;
            gripper.robotRoot=arm.transform;arm.gripPoint=gripper.attachmentPoint;
            for(int i=0;i<6;i++)arm.joints[i].transform.localRotation=Quaternion.AngleAxis(home[i],Axes[i]);
            GearboxRobotColours.ApplyTo(arm.transform);
            EditorUtility.SetDirty(arm);EditorUtility.SetDirty(gripper);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            File.WriteAllText(".simulation-check/yaskawa-build.txt","CAD simulation rig installed. Pedestal="+elevation+" m. Estimated pivots/limits/masses; not a physical robot calibration.\n");
            Debug.Log("CAD-derived six-axis Yaskawa simulation rig installed in GearboxTraining. Primitive scene backed up.");
        }
        private static void ConfigureCompactCell(GearboxAssemblyManager manager,RobotArmController arm) {
            var table=GameObject.Find("Workbench").transform;
            table.position=new Vector3(0,0,.05f);table.localScale=Vector3.one;
            var top=table.Find("Tabletop");top.localScale=new Vector3(2.6f,.1f,1.3f);
            foreach(Transform leg in table)if(leg.name.StartsWith("Leg")) {
                var p=leg.localPosition;p.x=Mathf.Sign(p.x)*1.1f;p.z=Mathf.Sign(p.z)*.45f;leg.localPosition=p;
            }
            var separate=GameObject.Find("Parts Table");if(separate!=null)separate.SetActive(false);
            var tray=GameObject.Find("Tray Base").transform.parent;
            tray.position=new Vector3(-.73f,1.025f,.15f);tray.localScale=Vector3.one;
            foreach(Transform child in tray) {
                if(child.name=="Tray Base")child.localScale=new Vector3(1.08f,.05f,1.1f);
                else if(child.name.StartsWith("Tray Rim")) {
                    child.gameObject.SetActive(false);
                    var p=child.localPosition;var s=child.localScale;
                    if(child.name.EndsWith("Front")||child.name.EndsWith("Back")){p.z=Mathf.Sign(p.z)*.53f;s.x=.94f;}
                    else {p.x=Mathf.Sign(p.x)*.45f;s.z=1.02f;}
                    child.localPosition=p;child.localScale=s;
                }
            }
            Physics.SyncTransforms();
            arm.transform.position=new Vector3(0,top.GetComponent<Collider>().bounds.max.y,.15f);
            var parts=manager.allParts.OrderBy(p=>p.partType).ThenBy(p=>p.partId).ToArray();
            for(int i=0;i<parts.Length;i++) {
                var p=parts[i];p.transform.SetParent(tray,true);
                p.transform.position=new Vector3(-.96f+i%3*.32f,1.052f,-.28f+i/3*.26f);
                Physics.SyncTransforms();
                float bottom=p.GetComponentsInChildren<Collider>().Min(c=>c.bounds.min.y);
                p.transform.position+=Vector3.up*Mathf.Max(0,1.065f-bottom);
            }
            var station=manager.phaseOne.InternalAssemblyRoot.parent;
            station.position=new Vector3(.65f,station.position.y,.3f);
            var mat=GameObject.Find("Assembly Mat").transform;
            var pos=mat.position;pos.x=.65f;mat.position=pos;
            var scale=mat.localScale;scale.x=.9f;scale.z=.6f;mat.localScale=scale;
            manager.phaseTwoInsertion.worker.SetPositionAndRotation(new Vector3(.7f,0,.96f),Quaternion.Euler(0,180,0));
            Camera.main.transform.position=new Vector3(3.1f,3,-4);
            Camera.main.transform.LookAt(new Vector3(0,1.1f,.2f));
            foreach(string name in new[]{"Robot Placeholder Position","Human Placeholder Position"}) {
                var marker=GameObject.Find(name);if(marker!=null)marker.SetActive(false);
            }
        }
        private static void AddVisual(Transform joint,string name,Vector3 pivot,Material material) {
            string asset=Folder+name+"_simulation.asset";
            Mesh mesh=AssetDatabase.LoadAssetAtPath<Mesh>(asset);
            if(mesh==null) {
                var vertices=File.ReadLines(Folder+name+".obj").Where(l=>l.StartsWith("v ")).Select(l=>{
                    var p=l.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
                    return new Vector3(float.Parse(p[1],CultureInfo.InvariantCulture),float.Parse(p[2],CultureInfo.InvariantCulture),float.Parse(p[3],CultureInfo.InvariantCulture))-pivot;
                }).ToArray();
                var triangles=File.ReadLines(Folder+name+".obj").Where(l=>l.StartsWith("f ")).SelectMany(l=>l.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(x=>int.Parse(x,CultureInfo.InvariantCulture)-1)).ToArray();
                mesh=new Mesh{name=name+" CAD simulation mesh",indexFormat=IndexFormat.UInt32};
                mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh,asset);
            }
            var visual=new GameObject(name+" CAD geometry");visual.transform.SetParent(joint,false);
            visual.AddComponent<MeshFilter>().sharedMesh=mesh;visual.AddComponent<MeshRenderer>().sharedMaterial=material;
            var envelope=visual.AddComponent<BoxCollider>();envelope.center=mesh.bounds.center;envelope.size=mesh.bounds.size;
        }
        public static void BuildBatch() { Build();EditorApplication.Exit(0); }
        public static void RebuildBatch() {
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/GearboxTraining_PrimitiveBackup.unity");
            EditorSceneManager.SaveScene(scene,ScenePath);
            Build();EditorApplication.Exit(0);
        }
        public static void CapturePreviewBatch() {
            EditorSceneManager.OpenScene(ScenePath);
            var camera=Camera.main;
            camera.transform.position=new Vector3(3.1f,3,-4);
            camera.transform.LookAt(new Vector3(0,1.1f,.2f));
            var target=new RenderTexture(1600,1000,24);
            camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var pixels=new Texture2D(1600,1000,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,1600,1000),0,0);pixels.Apply();
            File.WriteAllBytes(".simulation-check/tabletop-preview.png",pixels.EncodeToPNG());
            camera.targetTexture=null;RenderTexture.active=null;
            UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);
            EditorApplication.Exit(0);
        }
    }
}

