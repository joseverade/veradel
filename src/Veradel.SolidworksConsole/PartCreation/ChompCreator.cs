using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Veradel.SolidworksConsole.PartCreation
{
    public class ChompCreator
    {

        private SldWorks _swApp;
        private ModelDoc2 _model;
        private ModelDocExtension _ext;



        // Body
        private double _externalDiameter;
        private double _tickness;


        public ChompCreator(SldWorks swApp)
        {
            _swApp = swApp;
            _model = _swApp.ActiveDoc;
            _ext = _model.Extension;
        }

        // First body
        public void SetBody(double externalDiameter, double thickness)
        {
            _externalDiameter = externalDiameter;
            _tickness = thickness;
        }


        public void Create()
        {
            Body();
        }


        // Fisrt we create the body
        private void Body()
        {
            _ext.SelectByID2("ALZADO", "PLANE",0,0,0,false, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            _model.InsertSketch2(true);

            Point radius = new Point(0,_externalDiameter/2);
            SketchManager sManager = _model.SketchManager;

            _model.ClearSelection2(true);
            SketchSegment externalCircle = sManager.CreateCircleByRadius(0,0,0,radius.Y);
            externalCircle.Select4(false, null);
            _model.AddDiameterDimension2(radius.Y, radius.Y, 0);


            _model.ClearSelection2(true);
            _model.InsertSketch2(true);

            FeatureManager feat = _model.FeatureManager;
            feat.FeatureExtrusion3(true, false, false, (int)swEndConditions_e.swEndCondMidPlane, 0, _tickness / 1000.0, 0, false, false, false, false, 0,0, false, false, false, false, true, false, true, 0,0,false); 

        }

        // We set the geoemetry


    }
}
