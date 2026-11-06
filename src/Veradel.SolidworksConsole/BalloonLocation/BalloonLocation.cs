using SolidWorks.Interop.sldworks;

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
        }


    }
}
