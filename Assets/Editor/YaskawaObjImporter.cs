using UnityEditor;

namespace GearboxDemo.Editor
{
    public sealed class YaskawaObjImporter : AssetPostprocessor
    {
        private void OnPreprocessModel() {
            if(!assetPath.StartsWith("Assets/Models/YaskawaRobot/") || !assetPath.EndsWith(".obj"))return;
            var importer=(ModelImporter)assetImporter;
            importer.globalScale=1;
            importer.useFileScale=false;
            importer.importAnimation=false;
            importer.importCameras=false;
            importer.importLights=false;
            importer.addCollider=false;
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.importNormals=ModelImporterNormals.Calculate;
            importer.isReadable=false;
        }
    }
}
