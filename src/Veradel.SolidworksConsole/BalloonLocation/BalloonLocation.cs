using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Xml.Linq;

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

            List<SheetWrapper> sheet = SetSheetWrappers();

            foreach (SheetWrapper sheetWrapper in sheet)
            {
                // seteamos los globos 
                sheetWrapper.SetBalloons(drawingDoc);

                // seteamos las zonas
                sheetWrapper.SetZones();
            }

        }


        private List<SheetWrapper> SetSheetWrappers()
        {

            List<SheetWrapper> sheets = new List<SheetWrapper>();

            string[] names = drawingDoc.GetSheetNames();

            for (int i = 0; i < names.Length; i++)
            {
                Sheet sheet = drawingDoc.Sheet[names[i]];
                sheets.Add(new SheetWrapper(sheet, i + 1));
            }


            return sheets;
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


        public BalloonWrapper(int Page, double PosX, double PosY, string Text, string Zone)
        {
            this.Page = Page;
            this.PosX = PosX;
            this.PosY = PosY;
            this.Text = Text;
            this.Zone = Zone;

        }


    }


    public class SheetWrapper
    {
        public List<BalloonWrapper> Balloons;
        public List<double> LineY;
        public List<double> LineX;
        public string SheetName;
        public int SheetIndex;
        public int Width = 0;
        public int Height = 0;



        private Sheet SheetSw;
        private object[] Views;


        public SheetWrapper(Sheet SheetSw, int SheetIndex)
        {
            this.SheetSw = SheetSw;

            // Se llena el nombre automaticamente
            SheetName = SheetSw.GetName();
            this.SheetIndex = SheetIndex;

        }


        public void SetBalloons(DrawingDoc doc)
        {
            Balloons = new List<BalloonWrapper>();
            doc.ActivateSheet(SheetName);

            if (Views == null)
                SetView();

            foreach (View view in Views)
            {

                object[] oNotes = view.GetNotes();
                foreach (Note balloon in oNotes)
                {
                    if (balloon == null || !balloon.HasBalloon()) continue;

                    // Se rellena los globos

                    double[] info = balloon.GetBalloonInfo();
                    Balloons.Add(new BalloonWrapper(SheetIndex, info[0], info[1], balloon.GetText(), SheetSw.GetDrawingZone(info[0], info[1])));
                }
            }
        }



        // TODO: Set the lines and the views 
        public void SetZones()
        {

            SheetSw.GetSize(Width, Height);


            double step = 20 / 1000.0;

            string firstZone = SheetSw.GetDrawingZone(0, 0);


            string zoneToReview = SheetSw.GetDrawingZone(0, 0);






            for (int i = 0; i< Width; i++)
            {

                // Cambio de zona
                if (zoneToReview != SheetSw.GetDrawingZone(i, 0))
                {
                    double lowerLimit = i - step;
                    double upperLimit = i;

                    while (true)
                    {
                        double mean = (lowerLimit + upperLimit);

                        if (zoneToReview == )

                                
                    }

                }





            }





        }


        private void SetView()
        {
            Views = (object[])SheetSw.GetViews();
        }

    }


}
