using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Veradel.SolidworksConsole.Boilmaker
{
    public class BoilerMarkerTwo
    {

        public SldWorks SwApp;
        public ModelDoc2 Model;

        public DrawingDoc Drawing;

        public BoilerMarkerTwo(SldWorks SwApp)
        {
            this.SwApp = SwApp;
            Model = SwApp.ActiveDoc;            
            Drawing = Model as DrawingDoc;

            if (Drawing == null) return;

        }

        public void Execute()
        {




        }
    }




    public class ViewRegion
    {

    }

}
