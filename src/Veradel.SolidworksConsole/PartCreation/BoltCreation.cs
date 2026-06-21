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
    public class BoltCreation
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


        // const
        private const double angle_360 = Math.PI * 2;



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

            _swapp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);

            Revolution();

            if (_hasGroove)
                CutRevoluion();

            if (_hasChamfer)
                Chamfer();

            _swapp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);

        }

        private void Chamfer()
        {
            ModelDoc2 model = _swapp.ActiveDoc;
            ModelDocExtension ext = model.Extension;

                ext.SelectByID2("", "EDGE", (_l1 + _l2) / 1000.0, _d2 / 2000.0, 0, false, 0, null, (int)swSelectOption_e.swSelectOptionDefault);

            FeatureManager feat = model.FeatureManager;

            feat.InsertFeatureChamfer(4, (int)swChamferType_e.swChamferAngleDistance, _l3 / 1000.0, (_angle * Math.PI) / 180, 0, 0, 0, 0);



        }

        private void Revolution()
        {
            // First we select the "alzado plane"

            ModelDoc2 model = _swapp.ActiveDoc;
            ModelDocExtension ext = model.Extension;

            bool status = ext.SelectByID2("Alzado", "PLANE", 0, 0, 0, false, 0, null, 0);

            // sketchManager

            SketchManager sManager = model.SketchManager;
            sManager.InsertSketch(true);

            // center line
            SketchSegment centerLine = sManager.CreateLine(0, 0, 0, -10 / 1000.0, 0, 0);
            centerLine.ConstructionGeometry = true;


            // headLeftLine
            SketchSegment line1 = sManager.CreateLine(0, 0, 0, 0, _d1 / 2000.0, 0);
            line1.Select4(false, null);
            model.SketchAddConstraints("sgVERTICAL2D");


            // Lines
            SketchSegment line2 = sManager.CreateLine(0, _d1 / 2000.0, 0, _l1 / 1000, _d1 / 2000.0, 0);
            SketchSegment line3 = sManager.CreateLine(_l1 / 1000.0, _d1 / 2000.0, 0, _l1 / 1000.0, _d2 / 2000.0, 0);
            SketchSegment line4 = sManager.CreateLine(_l1 / 1000.0, _d2 / 2000.0, 0, (_l1 + _l2) / 1000, _d2 / 2000.0, 0);
            SketchSegment line5 = sManager.CreateLine((_l1 + _l2) / 1000.0, _d2 / 2000.0, 0, (_l1 + _l2) / 1000.0, 0, 0);
            SketchSegment close = sManager.CreateLine((_l1 + _l2) / 1000.0, 0, 0, 0, 0, 0);

            // Dimensions


            // d1
            model.ClearSelection2(true);
            line2.Select4(true, null);
            centerLine.Select4(true, null);
            model.AddDiameterDimension2(-10 / 1000.0, -10 / 1000.0, 0);

            //l1
            model.ClearSelection2(true);
            line2.Select4(true, null);
            model.AddHorizontalDimension2(0, 0, 0);

            // l2
            model.ClearSelection2(true);
            line4.Select4(true, null);
            model.AddHorizontalDimension2(0, 0, 0);

            // d2
            model.ClearSelection2(true);
            line4.Select4(true, null);
            centerLine.Select4(true, null);
            model.AddDiameterDimension2(40 / 1000.0, -10 / 1000.0, 0);


            model.ClearSelection2(true);

            model.InsertSketch2(true);

            FeatureManager featMan = model.FeatureManager;

            featMan.FeatureRevolve2(
                true, true, false, false, false, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                angle_360, 0, false, false, 0, 0, (int)swThinWallType_e.swThinWallOneDirection, 0.0, 0.0, true, false, false);


        }

        private void CutRevoluion()
        {
            Point centerLine1 = new Point(0, 0);
            Point centerLine2 = new Point(_l1 + _l2, 0);


            Point point1 = new Point(_l1 + _p1, _d2 / 2);
            Point point2 = new Point(_l1 + _p1, _d3 / 2);
            Point point3 = new Point(_l1 + _p1 + _e1, _d3 / 2);
            Point point4 = new Point(_l1 + _p1 + _e1, _d2 / 2);


            Point closure = point1;

            Point[] points = { point1, point2, point3, point4, closure };


            ModelDoc2 model = _swapp.ActiveDoc;
            ModelDocExtension ext = model.Extension;

            model.Extension.SelectByID2("Alzado", "PLANE", 0, 0, 0, false, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            model.InsertSketch2(true);

            SketchManager sManager = model.SketchManager;

            SketchSegment centerLine = sManager.CreateLine(centerLine1.X, centerLine1.Y, 0, centerLine2.X, centerLine2.Y, 0);
            centerLine.ConstructionGeometry = true;

            List<SketchSegment> segments = new List<SketchSegment>(points.Length);


            for (int i = 1; i < points.Length; i++)
            {
                SketchSegment segment = sManager.CreateLine(points[i - 1].X, points[i - 1].Y, 0, points[i].X, points[i].Y, 0);
                segments.Add(segment);
            }

            // Dimensions, fisrt we select the edge


            // p1
            model.ClearSelection2(true);
            ext.SelectByID2("", "EDGE", _l1 / 1000.0, _d2 / 2000.0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            segments[0].Select4(true, null);
            model.AddHorizontalDimension2(0, 0, 0);

            // e1
            model.ClearSelection2(true);
            segments[3].Select4(true, null);
            model.AddHorizontalDimension2(0, 0, 0);

            // d3
            model.ClearSelection2(true);
            centerLine.Select4(true, null);
            segments[1].Select4(true, null);
            model.AddDiameterDimension2(0, -10 / 1000.0, 0);

            model.InsertSketch();



            // Feature
            FeatureManager feat = model.FeatureManager;
            feat.FeatureRevolve2(true, true, false, true, false, true,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                angle_360, 0, false, false, 0, 0, (int)swThinWallType_e.swThinWallOneDirection, 0, 0, true, false, true);



        }

    }


    public class Point
    {
        public double X { get; private set; }
        public double Y { get; private set; }

        public Point(double x, double y)
        {
            X = x / 1000.0;
            Y = y / 1000.0;
        }

        public override string ToString()
        {
            return $"({X.ToString()}, {Y.ToString()})";
        }

    }
}
