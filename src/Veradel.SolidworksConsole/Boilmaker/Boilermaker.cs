using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Veradel.SolidworksConsole.Boilmaker
{
    public class Boilermaker
    {

        private SldWorks _swApp;

        public Boilermaker(SldWorks swApp)
        {
            _swApp = swApp;






        }

        public void Run()
        {
            // setters

            ModelDoc2 modelDrawing = null;
            modelDrawing = _swApp.ActiveDoc;

            string drawingPathName = modelDrawing.GetPathName();

            DrawingDoc drawing = null;
            drawing = modelDrawing as DrawingDoc;

            if (drawing == null) return;

            // We open the referenced drawing, the button must show in parts only just like de color edge

            Sheet activeSheet = null;
            activeSheet = drawing.GetCurrentSheet();

            // datos debug:
            double width = 0, height = 0;
            activeSheet.GetSize(ref width, ref height);


            // select the first view 
            object[] oViews = activeSheet.GetViews();
            View firstView = (View)oViews[0];

            ModelDoc2 referencedPartInDrawing = firstView.ReferencedDocument;
            string referencedDrawingPathName = referencedPartInDrawing.GetPathName();


            // opendoc
            ModelDoc2 modelPart = OpenReferencedDoc(referencedPartInDrawing.GetPathName());
            PartDoc partDoc = modelPart as PartDoc;


            // Se verifica la operacion is weldment
            FeatureManager featureManager = modelPart.FeatureManager;
            if (!partDoc.IsWeldment())
                featureManager.InsertWeldmentFeature();

            // una vez verificada que sea weldment, se recorre todas las carpetas y se extrae una pieza

            // El objetivo es obtener todas las cut-list, para esto a partir de la version 2024: https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IConfiguration~GetCutListItems.html


            Feature weldmentFeature = null;
            weldmentFeature = partDoc.FirstFeature();

            // Ahora se hace el array:
            List<Body2> bodies = GetBodiesCutList(weldmentFeature);

            // regresamos al dibujo

            int errors = 0;
            ModelDoc2 abc = _swApp.ActivateDoc3(drawingPathName, true, 1, ref errors);

            SelectionMgr sel = modelPart.SelectionManager;



            for (int i = 0; i < bodies.Count; i++)
            {

                Face2 principalFace = GetOrientationVector(bodies[i]);      // cara principal
                Face2 nextFace = principalFace.GetNextFace();               // cara secundaria
                swSurfaceTypes_e surType = (swSurfaceTypes_e)((Surface)nextFace.GetSurface()).Identity();



                //View vista = drawing.CreateDrawViewFromModelView3(referencedDrawingPathName, "*Izquierda", 0, 0, 0);

                modelDrawing.ClearSelection2(true);
                sel.SuspendSelectionList();

                SelectData seldata = sel.CreateSelectData();
                seldata.Mark = 1;
                bool append1 = sel.AddSelectionListObject(principalFace, seldata);

                SelectData seldata2 = sel.CreateSelectData();
                seldata2.Mark = 2;
                bool append2 = sel.AddSelectionListObject(nextFace, seldata2);

               

                View vista = drawing.CreateRelativeView(referencedDrawingPathName, 0,0, (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_FRONT, (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_RIGHT);


                DispatchWrapper[] bodyWrappers = new DispatchWrapper[1];
                bodyWrappers[0] = new DispatchWrapper(bodies[i]);

                vista.Bodies = bodyWrappers;


                double[] afterPos = vista.GetOutline();
                double[] newPos = { -afterPos[0], -afterPos[1] };
                vista.Position = newPos;
                sel.ResumeSelectionList2(false);
                modelDrawing.ClearSelection2(true);

            }





        }

        private List<Body2> GetBodiesCutList(Feature weldmentFeature)
        {

            List<Body2> bodies = new List<Body2>();

            bool exit = false;
            bool entro = false;

            do
            {
                switch (weldmentFeature.GetTypeName2())
                {
                    case "CutListFolder":
                        entro = true;
                        BodyFolder folder = null;
                        folder = weldmentFeature.GetSpecificFeature2();

                        Console.WriteLine(weldmentFeature.Name);

                        object[] oBodies = folder.GetBodies();

                        if (oBodies != null)
                            bodies.Add((Body2)oBodies[0]);


                        Marshal.ReleaseComObject(folder);
                        for (int i = 1; i < oBodies?.Length; i++)
                            Marshal.ReleaseComObject(oBodies[i]);


                        break;

                    default:
                        if (entro)
                            exit = true;
                        break;
                }

                if (exit) break;

                weldmentFeature = weldmentFeature.GetNextFeature();
            } while (weldmentFeature != null);

            return bodies;
        }



        private Face2 GetOrientationVector(Body2 Body)
        {
            object[] oFaces = Body.GetFaces();


            Face2 actualFace = (Face2)oFaces[0];


            for (int i = 1; i < oFaces?.Length; i++)
            {

                Face2 faceToCompare = null;
                faceToCompare = oFaces[i] as Face2;

                Surface surf = null;
                surf = faceToCompare.GetSurface();


                if (faceToCompare == null || faceToCompare.GetShellType() != 2 || surf.Identity() != (int)swSurfaceTypes_e.PLANE_TYPE)
                {
                    Marshal.ReleaseComObject(faceToCompare);
                    Marshal.ReleaseComObject(surf);
                    continue;
                }


                if (actualFace.GetArea() < faceToCompare.GetArea())
                {
                    var temp = actualFace;
                    actualFace = faceToCompare;
                    Marshal.ReleaseComObject(temp);
                }
                else
                {
                    Marshal.ReleaseComObject(faceToCompare);
                }
            }


            return actualFace;

        }

        private ModelDoc2 OpenReferencedDoc(string path)
        {

            int errors = 0, warnings = 0;
            ModelDoc2 referenced = _swApp.OpenDoc6(path, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty, ref errors, ref warnings);

            if (referenced == null)
                throw new Exception("The file cannot be accessed");


            if (warnings == (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen)
            {
                _swApp.ActivateDoc3(path, true, (int)swRebuildOnActivation_e.swUserDecision, errors);
            }


            return referenced;
        }


    }
}
