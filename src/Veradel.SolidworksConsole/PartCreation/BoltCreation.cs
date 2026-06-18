using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Veradel.SolidworksConsole.PartCreation
{
    public  class BoltCreation
    {
        private SldWorks _swapp;

        // First feature body
        private double _d1; 
        private double _l1;
        private double _d2;
        private double _l2;

        // Second feature
        private bool _hasGroove = false;
        private double _p1;
        private double _e1;
        private double _d3;

        // chamfer
        private bool _hasChamfer = false;
        private double _angle;
        private double _l3;



        public BoltCreation(SldWorks swapp, double d1, double l1, double d2, double l2)
        {
            _swapp = swapp;
            _d1 = d1;
            _l1 = l1;
            _d2 = d2;
            _l2 = l2;
        }

        public void SetGroove(double p1, double e1, double d3)
        {
            _hasGroove = true;
            _p1 = p1;
            _e1 = e1;
            _d3 = d3;
        }

        public void SetChamfer(double angle, double l3)
        {
            _hasChamfer = true;
            _angle = angle;
            _l3 = l3;
        }




        public void CreateBolt()
        {
            // 3 features
            // Revolution body
            // cut revolution
            // chamfer

            Revolution();

        }
        
        private void Revolution()
        {
            // First we select the "alzado plane"

            ModelDoc2 model = _swapp.ActiveDoc;
            ModelDocExtension ext = model.Extension;

            bool status = ext.SelectByID2("Alzado", "PLANE", 0, 0, 0, false, 0, null, 0);
            model.SketchManager.InsertSketch(true);

            // center line



        }


    }
}
