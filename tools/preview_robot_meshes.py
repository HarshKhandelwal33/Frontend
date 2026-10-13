"""Render converted CAD geometry for visual conversion checks."""
import sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.tools' / 'cad-python'))
import vtk

renderer = vtk.vtkRenderer()
renderer.SetBackground(0.93, 0.95, 0.97)
for index, name in enumerate(('base', 'saxis', 'laxis', 'uaxis', 'raxis', 'baxis', 'taxis')):
    reader = vtk.vtkOBJReader()
    reader.SetFileName(str(ROOT / 'Assets' / 'Models' / 'YaskawaRobot' / (name + '.obj')))
    mapper = vtk.vtkPolyDataMapper()
    mapper.SetInputConnection(reader.GetOutputPort())
    actor = vtk.vtkActor()
    actor.SetMapper(mapper)
    actor.GetProperty().SetColor(*( (0.15, 0.24, 0.31) if index in (0, 6) else (0.25, 0.51, 0.74) ))
    renderer.AddActor(actor)
camera = renderer.GetActiveCamera()
camera.SetPosition(1.8, 1.4, 2.1)
camera.SetFocalPoint(0.25, 0.47, 0)
camera.SetViewUp(0, 1, 0)
renderer.ResetCamera()
window = vtk.vtkRenderWindow()
window.SetOffScreenRendering(1)
window.SetSize(1000, 900)
window.AddRenderer(renderer)
window.Render()
capture = vtk.vtkWindowToImageFilter()
capture.SetInput(window)
capture.Update()
writer = vtk.vtkPNGWriter()
writer.SetFileName(str(ROOT / '.simulation-check' / 'yaskawa-cad-preview.png'))
writer.SetInputConnection(capture.GetOutputPort())
writer.Write()
window.Finalize()
