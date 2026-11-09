using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;

namespace Veradel.SolidworksConsole.BalloonLocation
{
    public class BalloonLocation
    {


        /*
         * PROBLEMA: Nativamente en solidoworks no hay forma de identificar la posicion de los globos en base a la LDM
         * Solucion: Identificar todos los globos y sus posiciones
         * 
         */



        private ModelDoc2 modelDoc2;
        private DrawingDoc drawingDoc;


        public BalloonLocation(ModelDoc2 modelDoc2)
        {
            this.modelDoc2 = modelDoc2;
            drawingDoc = modelDoc2 as DrawingDoc;
        }

        public void Execute()
        {
            if (drawingDoc == null) return;

            List<BalloonWrapper> balloons = new List<BalloonWrapper>();

            int sheetCount = 1;

            foreach (string name in GetSheetNames())
            {
                
                drawingDoc.ActivateSheet(name);

                Sheet activeSheet = drawingDoc.Sheet[name];

                object[] oView = (object[])activeSheet.GetViews(); 


                foreach (View view in oView)
                {
                    object[] oNotes =  view.GetNotes();


                    foreach (Note balloon in oNotes)
                    {
                        if (balloon == null || !balloon.HasBalloon()) continue; // note's a ballooon                        
                        balloons.Add(GetBallonWrapper(balloon, sheetCount, view.Name, activeSheet));

                        // TODO: Se debe verificar que la nota no salga del tamaño de la hoja
                    }

                }

                sheetCount += 1;

            }



        }



        private void GetBalloons()
        {

        }


        private string[] GetSheetNames()
        {
            return drawingDoc.GetSheetNames();
        }

        private BalloonWrapper GetBallonWrapper(Note Note, int SheetCount, string viewName, Sheet activeSheet)
        {

            BalloonWrapper balloon = new BalloonWrapper();
            balloon.Text = Note.GetText();

            double[] info = Note.GetBalloonInfo();
            balloon.PosX = info[0];
            balloon.PosY = info[1];
            balloon.Page = SheetCount;
            balloon.ViewName = viewName;
            balloon.Zone = activeSheet.GetDrawingZone(balloon.PosX, balloon.PosY);

            return balloon;
        }



    }


    public class BalloonWrapper
    {

        public int Page;
        public double PosX;
        public double PosY;
        public string Text;
        public string ViewName;

        public string Zone;

        public void GetValues()
        {
            // format: page - 

        }

    }

}
