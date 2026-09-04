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

                BoilermakerDebug.PrintNormalValues(principalFace);
                BoilermakerDebug.PrintNormalValues(nextFace);



                modelDrawing.ClearSelection2(true);
                sel.SuspendSelectionList();

                SelectData seldata = sel.CreateSelectData();
                seldata.Mark = 1;
                bool append1 = sel.AddSelectionListObject(principalFace, seldata);

                SelectData seldata2 = sel.CreateSelectData();
                seldata2.Mark = 2;
                bool append2 = sel.AddSelectionListObject(nextFace, seldata2);

                View vista = drawing.CreateRelativeView(referencedDrawingPathName, 0, 0, (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_FRONT, (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_RIGHT);


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


                double actualArea = actualFace.GetArea();
                double faceToCompareArea = faceToCompare.GetArea();

                Console.WriteLine($"actualArea: {actualArea}, faceToCompare: {faceToCompareArea}");


                if (actualArea < faceToCompareArea)
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

            Console.WriteLine($"Area mas grande: {actualFace.GetArea()}");
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



        private Face2 GetRightFace(Face2 SelectedFace)
        {

            object[] oEdges = SelectedFace.GetEdges();


            // objetivo: buscar un edge que tenga los vectores canonicos y que NO sea pararelo a la normal de la cara


            bool encontrado = false;

            /*
            foreach (object oEdge in oEdges)
            {

                Edge edge = oEdge as Edge;

                if (edge == null)
                {
                    Marshal.ReleaseComObject(oEdge);
                    continue;
                }


                // obtenemos el tipo de curva:

                Curve curva = null;
                curva = edge.GetCurve();

                if (curva == null)
                {
                    Marshal.ReleaseComObject(edge);
                    Marshal.ReleaseComObject(oEdge);
                    continue;
                }

                int tipoCurva = curva.Identity();

                // Si el edge no es una linea
                if (tipoCurva != (int)swCurveTypes_e.LINE_TYPE)
                {
                    Marshal.ReleaseComObject(edge);
                    Marshal.ReleaseComObject(oEdge);
                    Marshal.ReleaseComObject(curva);
                    continue;
                }

                // liberamos la curva
                Marshal.ReleaseComObject(curva);



¡
                // Si el edge es una linea, buscar si tiene direccion de vectores canonicos y que no sea paralelo a la normal de la cara grande

                Vertex vertex1 = null, vertex2 = null;

                vertex1 = edge.GetStartVertex();
                vertex2 = edge.GetEndVertex();

                double[] point1 = vertex1.GetPoint();
                double[] point2 = vertex2.GetPoint();


                // formamos el vector
                double[] vectorEdge = new double[3];
                vectorEdge[0] = point2[0] - point1[0];
                vectorEdge[1] = point2[1] - point1[1];
                vectorEdge[2] = point2[2] - point1[2];

                // calculamos si el vector es canonico





                // Si no se encuentra vector canonico entros regresar null

            }
             */


            // El plan es encontrar unas aristas que formen 90 grados 

            





        }



    }


    public static class VeraDelMathClass
    {
        public static double DotProduct(double[] v1, double[] v2)
        {
            if (v1.Length != v2.Length || (v1.Length != 3 && v1.Length != 3)) throw new Exception("Vector length not equal");


            double x = v1[0] * v2[0];
            double y = v1[1] * v2[1];
            double z = v1[2] * v2[2];
            return x + y + z;

        }


        public static double[] CrossProduct(double[] v1, double[] v2)
        {

            double x = v1[1] * v2[2] - v2[1] * v1[2];
            double y = v1[0] * v2[2] - v2[0] * v1[2];
            double z = v1[0] * v2[1] - v2[0] * v1[1];

            return new double[] { x, y, z };
        }

    }


    public static class BoilermakerDebug
    {

        public static void PrintNormalValues(Face2 face)
        {
            double[] normal = face.Normal;

            Console.WriteLine($"Valores de la normal: ({normal?[0]},{normal?[1]},{normal?[2]})");

        }

    }

}
