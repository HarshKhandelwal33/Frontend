"""Convert supplied IGES link geometry to metre-scale Unity OBJ meshes.

Install cadquery-ocp into .tools/cad-python; source CAD files are read only.
"""
import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.tools' / 'cad-python'))
from OCP.IGESControl import IGESControl_Reader
from OCP.IFSelect import IFSelect_RetDone
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.BRep import BRep_Tool
from OCP.TopAbs import TopAbs_FACE, TopAbs_REVERSED
from OCP.TopExp import TopExp_Explorer
from OCP.TopoDS import TopoDS
from OCP.TopLoc import TopLoc_Location


def convert(source, target):
    reader = IGESControl_Reader()
    if reader.ReadFile(str(source)) != IFSelect_RetDone:
        raise RuntimeError(f'Cannot read {source}')
    reader.TransferRoots()
    shape = reader.OneShape()
    if shape.IsNull():
        raise RuntimeError(f'No geometry in {source}')
    BRepMesh_IncrementalMesh(shape, 0.5, False, 0.3, True)
    vertices, triangles = [], []
    faces = TopExp_Explorer(shape, TopAbs_FACE)
    while faces.More():
        face = TopoDS.Face(faces.Current())
        location = TopLoc_Location()
        mesh = BRep_Tool.Triangulation_s(face, location)
        if mesh is not None:
            offset = len(vertices)
            for index in range(1, mesh.NbNodes() + 1):
                point = mesh.Node(index).Transformed(location.Transformation())
                # This source assembly is Y-up, in millimetres. Reflect X for Unity.
                vertices.append((-point.X()/1000, point.Y()/1000, point.Z()/1000))
            for index in range(1, mesh.NbTriangles() + 1):
                a, b, c = mesh.Triangle(index).Get()
                if face.Orientation() != TopAbs_REVERSED:
                    b, c = c, b
                triangles.append((a+offset, b+offset, c+offset))
        faces.Next()
    if not triangles:
        raise RuntimeError(f'No triangles in {source}')
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('w', encoding='utf-8', newline='\n') as output:
        output.write(f'# Converted from {source.name}; metres, Y up\no {target.stem}\n')
        for point in vertices:
            output.write('v %.8f %.8f %.8f\n' % point)
        for face in triangles:
            output.write('f %d %d %d\n' % face)
    bounds = [[min(p[i] for p in vertices), max(p[i] for p in vertices)] for i in range(3)]
    return dict(source=str(source), sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
                mesh=target.name, vertices=len(vertices), triangles=len(triangles), bounds=bounds)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('source', type=Path)
    args = parser.parse_args()
    output = ROOT / 'Assets' / 'Models' / 'YaskawaRobot'
    report = []
    for link in ('base', 'saxis', 'laxis', 'uaxis', 'raxis', 'baxis', 'taxis'):
        candidates = list(args.source.glob(link + '_view_*.igs'))
        if len(candidates) != 1:
            raise RuntimeError(f'Expected exactly one {link} IGES file')
        item = convert(candidates[0], output / (link + '.obj'))
        report.append(item)
        print(json.dumps(item), flush=True)
    (output / 'conversion.json').write_text(json.dumps(report, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
