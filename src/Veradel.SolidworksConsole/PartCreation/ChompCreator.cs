using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Schema;

namespace Veradel.SolidworksConsole.PartCreation
{
    public class ChompCreator
    {

        private SldWorks _swApp;
        private ModelDoc2 _model;
        private ModelDocExtension _ext;

        // Body
        private double _externalDiameter;
        private double _thickness;

        // Cut roller housing
        private Step[] _steps;
        private double _positionY;
        private int _nRollers;
        private double _distanceBetweenRollers;


        // Cut body
        private double _dFromRevolveToBottom;
        private double _totalHeight;

        // Second Cut Body
        private double _dFromTop;
        private double _secondCutWidth;

        private bool _chanferCut = false;

        // Chamfer Body Cut
        private double _hDimension1;
        private double _hDimension2;
        private double _hDimension3;
        private double _vDimension;


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
            _thickness = thickness;
        }

        // Roller housing
        public void SetRollerHousing(Step[] steps, double positionY, int nRollers, double distanceBetweenRollers)
        {
            _steps = steps;
            _positionY = positionY;
            _nRollers = nRollers;
            _distanceBetweenRollers = distanceBetweenRollers;
        }

        // First bodycut
        public void SetBodyCut(double dFromRevolveToBottom, double totalHeight)
        {
            _dFromRevolveToBottom = dFromRevolveToBottom;
            _totalHeight = totalHeight;
        }

        // Second body cut
        public void SetSecondBodyCut(double dFromTop, double secondCutWidth)
        {
            // Distance From the top
            _dFromTop = dFromTop;
            _secondCutWidth = secondCutWidth;
        }

        // Chamfer Body Cut
        public void SetChamferBodyCut(double hDimension1, double hDimension2, double hDimension3, double vDimension)
        {
            _chanferCut = true;

            _hDimension1 = hDimension1;
            _hDimension2 = hDimension2;
            _hDimension3 = hDimension3;
            _vDimension = vDimension;
        }


        // Here we set types of bodycuts, the firt one is chamfer tyoe
        // Chamfer BodyCut


        public void Create()
        {
            _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            Body();
            StepsAndRevolvedCut();
            LinearPattern();
            BodyCut();
            SecondBodyCut();

            if (_chanferCut)
            {
                ChamferBodyCut();
            }

            _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);
        }


        // Fisrt we create the body
        private void Body()
        {
            _ext.SelectByID2("ALZADO", "PLANE", 0, 0, 0, false, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            _model.InsertSketch2(true);

            Point radius = new Point(0, _externalDiameter / 2);
            SketchManager sManager = _model.SketchManager;

            _model.ClearSelection2(true);
            SketchSegment externalCircle = sManager.CreateCircleByRadius(0, 0, 0, radius.Y);
            externalCircle.Select4(false, null);
            _model.AddDiameterDimension2(radius.Y, radius.Y, 0);


            _model.ClearSelection2(true);
            _model.InsertSketch2(true);

            FeatureManager feat = _model.FeatureManager;
            feat.FeatureExtrusion3(true, false, false, (int)swEndConditions_e.swEndCondMidPlane, 0, _thickness / 1000.0, 0, false, false, false, false, 0, 0, false, false, false, false, true, false, true, 0, 0, false);

        }

        // We create the revolved cut
        private void StepsAndRevolvedCut()
        {
            // Fisrt we set the position of the cuts

            // Creation of the plan

            // even steps

            double posx = (_nRollers - 1) * _distanceBetweenRollers / 2;


            // convert to mm
            posx = posx / 1000.0;

            _ext.SelectByID2("Vista Lateral", "PLANE", 0, 0, 0, false, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            FeatureManager feat = _model.FeatureManager;
            RefPlane plane = feat.InsertRefPlane((int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance + (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip, posx, 0, 0, 0, 0);

            // we create the construction line (axis for revolve)
            Point startPoint = new Point(-_thickness / 2, _positionY);
            Point endPoint = new Point((-_thickness / 2) - 20, _positionY);

            ((Entity)plane).Select4(false, null);
            _model.InsertSketch2(true);

            SketchManager sManager = _model.SketchManager;
            SketchSegment revLine = sManager.CreateLine(startPoint.X, startPoint.Y, 0, endPoint.X, endPoint.Y, 0);
            revLine.ConstructionGeometry = true;

            revLine.Select4(false, null);
            _model.AddHorizontalDimension2(0, 0, 0);

            // 0. -35, 34/2
            // 1. -35+5.5, 34/2
            // 2. -35+5.5, 29/2
            // 3. -35+5.5+8.5, 29/2
            // 4. -35+5.5+8.5, 32/2
            // 5. -35+5.5+8.5+47.8, 32/2
            // 6. -35+5.5+8.5+47.8, 33/2
            // 7. -35+5.5+8.5+47.8+1.7, 33/2
            // 8. -35+5.5+8.5+47.8+1.7, 32/2
            // 9. -35+5.5+8.5+47.8+1.7+6.5,32/2

            // Convert the steps to points


            double xSum = -(_thickness / 2);


            List<Point> points = new List<Point> { startPoint };

            int xCounter = 0;
            int yCounter = 0;

            for (int i = 0; i < (_steps.Length * 2) - 1; i++)
            {

                if (i != 0 && i % 2 != 0)
                {
                    xSum = xSum + _steps[xCounter].Length;
                    xCounter++;
                }

                // Ignore the first and change 
                if (i != 0 && i % 2 == 0)
                    yCounter++;


                Point pointToInsert = new Point(xSum, _steps[yCounter].Diameter / 2.0 + _positionY);
                Console.WriteLine(pointToInsert.ToString());

                points.Add(pointToInsert);
            }


            xSum = _thickness / 2;
            points.Add(new Point(xSum, _steps[yCounter].Diameter / 2));


            // endinpoint point
            points.Add(new Point(xSum, _positionY));


            List<SketchSegment> segments = new List<SketchSegment>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                SketchSegment seg = _model.CreateLine2(points[i].X, points[i].Y, 0, points[i + 1].X, points[i + 1].Y, 0);


                if (points[i].X == points[i + 1].X)
                {
                    seg.Select4(false, null);
                    _model.SketchAddConstraints("sgVERTICAL2D");
                }
                else if (points[i].Y == points[i + 1].Y)
                {
                    seg.Select4(false, null);
                    _model.SketchAddConstraints("sgHORIZONTAL2D");
                    // Horizontal lines will define the 2d geometry


                    _model.ClearSelection2(true);
                    seg.Select4(true, null);
                    revLine.Select4(true, null);
                    _model.AddDiameterDimension2(0, 0, 0);

                    // jumps the last one   
                    if (i != points.Count - 1)
                    {
                        _model.ClearSelection2(true);
                        seg.Select4(false, null);
                        _model.AddHorizontalDimension2(0, 0, 0);
                    }


                }
                segments.Add(seg);
            }


            // Connect the end and the last point

            SketchSegment lastSeg = _model.CreateLine2(points[0].X, points[0].Y, 0, points[points.Count - 1].X, points[points.Count - 1].Y, 0);

            // exit the
            _model.InsertSketch2(true);

            // Revolve cut
            feat.FeatureRevolve2(true, true, false, true, false, true,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                2 * Math.PI, 0, false, false, 0, 0,
                (int)swThinWallType_e.swThinWallOneDirection, (int)swThinWallType_e.swThinWallOneDirection,
                0, true, false, true);



        }

        // Linear pattern of the fisrt
        private void LinearPattern()
        {
            _ext.GetLastFeatureAdded().Select2(false, 4);

            _ext.SelectByID2("Vista lateral", "PLANE", 0, 0, 0, true, 1, null, (int)swSelectOption_e.swSelectOptionDefault);
            _ext.SelectByID2("Planta", "PLANE", 0, 0, 0, true, 2, null, (int)swSelectOption_e.swSelectOptionDefault);

            FeatureManager feat = _model.FeatureManager;

            LinearPatternFeatureData swLinearPatternFeatureData = feat.CreateDefinition((int)swFeatureNameID_e.swFmLPattern);

            swLinearPatternFeatureData.D1EndCondition = 0;
            swLinearPatternFeatureData.D1ReverseDirection = false;
            swLinearPatternFeatureData.D1Spacing = _distanceBetweenRollers / 1000;
            swLinearPatternFeatureData.D1TotalInstances = 8;
            swLinearPatternFeatureData.D2EndCondition = 0;
            swLinearPatternFeatureData.D2PatternSeedOnly = false;
            swLinearPatternFeatureData.D2ReverseDirection = false;
            swLinearPatternFeatureData.D2Spacing = 0.01;
            swLinearPatternFeatureData.D2TotalInstances = 1;
            swLinearPatternFeatureData.GeometryPattern = false;
            swLinearPatternFeatureData.VarySketch = false;
            ;
            feat.CreateFeature(swLinearPatternFeatureData);

        }


        // We select the face to make the body cut
        private void BodyCut()
        {

            Point startingPoint = new Point(0, _externalDiameter / 4000.0);

            _model.ClearSelection2(true);
            _ext.SelectByRay(0, _externalDiameter / 4000.0, 0, 0, 0, 1, 1, (int)swSelectType_e.swSelFACES, false, 0, (int)swSelectOption_e.swSelectOptionDefault);

            _model.InsertSketch2(true);

            SketchManager sMan = _model.SketchManager;


            // The bottom line is defined by a dimension that starts in the center of the housing and ends it the line
            // the total is defined by dimensioning the left line
            // the width is tangets


            // NOTE: here must be math in order that the feature don't cut the housings, added 100 mm to x in both size in order to get a margin, to the coincidence mate doesn't modify the dimensions
            object[] segs = sMan.CreateCornerRectangle((-_externalDiameter - 100) / 2000.0,
                (_positionY - _dFromRevolveToBottom) / 1000.0, 0,
                (_externalDiameter + 100) / 2000.0,
                (_totalHeight - _positionY - _dFromRevolveToBottom) / 1000.0, 0);

            _model.ClearSelection2(true);

            // the selection starts from the bottom and then goes clockwise
            /*
            for (int i = 0; i < segs.Length; i++)
            {
                ((SketchSegment)segs[i]).Select4(true, null);
            }
             */

            // click the last housing circle (right one) and the bottom line

            double posx = ((_nRollers - 1) * _distanceBetweenRollers / 2 + (_steps[0].Diameter) / 2) / 1000.0;

            _ext.SelectByID2("", "EDGE", posx, _positionY, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            ((SketchSegment)segs[0]).Select4(true, null);

            _model.AddVerticalDimension2(0, 0, 0);

            _model.ClearSelection2(true);
            ((SketchSegment)segs[3]).Select4(false, null);
            _model.AddVerticalDimension2(0, 0, 0);


            // tangents

            _model.ClearSelection2(true);


            _ext.SelectByID2("", "EDGE", _externalDiameter / 2000.0, 00, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            ((SketchSegment)segs[1]).Select4(true, null);
            _model.SketchAddConstraints("sgTANGENT");


            _model.ClearSelection2(true);
            _ext.SelectByID2("", "EDGE", _externalDiameter / 2000.0, 00, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            ((SketchSegment)segs[3]).Select4(true, null);
            _model.SketchAddConstraints("sgTANGENT");


            _model.InsertSketch2(true);

            FeatureManager feat = _model.FeatureManager;
            feat.FeatureCut4(true, true, false,
                (int)swEndConditions_e.swEndCondThroughAll,
                0, 0, 0, false, false, false, false, 0, 0,
                false, false, false, false, false, false, true,
                false, false, false, (int)swStartConditions_e.swStartSketchPlane, 0, false, false);

        }


        // Here the width must be less than the top lenght
        private void SecondBodyCut()
        {
            // Select the same face, like the first cut, it's the same selected face but it select a minor Y point, because, the body's cut
            _model.ClearSelection2(true);
            double topLinePosY = _positionY - _dFromRevolveToBottom + _totalHeight;
            bool status = _ext.SelectByRay(0, topLinePosY / 1000.0, 0, 0, 0, 1, 1, (int)swSelectType_e.swSelFACES,
                false, 0, (int)swSelectOption_e.swSelectOptionDefault);
            _model.InsertSketch2(true);

            SketchManager sMan = _model.SketchManager;
            // The top line


            object[] segments = sMan.CreateCornerRectangle((-_secondCutWidth) / 2000.0, (topLinePosY - _dFromTop) / 1000.0, 0, (_secondCutWidth) / 2000.0, topLinePosY / 1000.0, 0);


            // select the bottom:

            // we define the top line

            ((SketchSegment)segments[2]).Select4(false, null);

            DisplayDimension dtop = _model.AddHorizontalDimension2(0, 0, 0);
            dtop.GetDimension2(0).SetSystemValue3(_secondCutWidth / 1000, (int)swSetValueInConfiguration_e.swSetValue_InAllConfigurations, null);


            // we create a mid point to fully define the geometry
            SketchSegment middleLine = sMan.CreateLine(0, 0, 0, 0, 10 / 1000.0, 0);
            _model.ClearSelection2(true);
            middleLine.ConstructionGeometry = true;

            ((SketchSegment)segments[0]).Select4(true, null);
            _model.SelectMidpoint();

            ((SketchPoint)((SketchLine)middleLine).GetEndPoint2()).Select4(true, null);

            _model.SketchAddConstraints("sgCOINCIDENT");

            _model.ClearSelection2(true);
            ((SketchSegment)segments[3]).Select4(false, null);

            _ext.AddDimension(0, 0, 0, (int)swSmartDimensionDirection_e.swSmartDimensionDirection_Right);

            _model.InsertSketch2(true);

            FeatureManager feat = _model.FeatureManager;
            feat.FeatureCut4(true, false, false,
                (int)swEndConditions_e.swEndCondThroughAll,
                0, 0, 0, false, false, false, false, 0, 0,
                false, false, false, false, false, false, true,
                false, false, false, (int)swStartConditions_e.swStartSketchPlane, 0, false, false);


        }
        private void ChamferBodyCut()
        {

            // First we select the face to cut, you have to mathematically calcultate if this point will select the face, if not then create create something
            _model.ClearSelection2(true);
            double topLinePosY = _positionY - _dFromRevolveToBottom + _totalHeight - _dFromTop - 2; //harcoded a margin
            bool status = _ext.SelectByRay(0, topLinePosY / 1000.0, 0, 0, 0, 1, 1, (int)swSelectType_e.swSelFACES,
                false, 0, (int)swSelectOption_e.swSelectOptionDefault);

            // first we create the line, it start from the left housing

            double posx = (_nRollers - 1) * _distanceBetweenRollers / 2;

            Point auxStartPoint = new Point(-posx, _positionY); // Center
            Point auxEndPoint = new Point(-posx, _positionY - _dFromRevolveToBottom);   // Bottom


            _model.InsertSketch2(true);
            SketchManager sMan = _model.SketchManager;
            SketchSegment auxLine1 = sMan.CreateLine(auxStartPoint.X, auxStartPoint.Y, 0, auxEndPoint.X, auxEndPoint.Y, 0);
            auxLine1.ConstructionGeometry = true;

            // selection of the external edge of the housing

            _model.ClearSelection2(true);

            bool status2 = _ext.SelectByID2("", "EDGE", auxStartPoint.X + (_steps[0].Diameter) / 2000.0,
                _positionY, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);

            SketchPoint sketchStartPoint = ((SketchLine)auxLine1).GetStartPoint2();
            sketchStartPoint.Select4(true, null);

            _model.SketchAddConstraints("sgCONCENTRIC");

            _model.ClearSelection2(true);

            //2nd aux line
            Point auxStartPoint2 = new Point(-posx + _distanceBetweenRollers, _positionY);
            Point auxEndPoint2 = new Point(-posx + _distanceBetweenRollers, -_dFromRevolveToBottom);   // Bottom

            SketchSegment auxLine2 = sMan.CreateLine(auxStartPoint2.X, auxEndPoint2.Y, 0, auxEndPoint2.X, auxEndPoint2.Y, 0);


            // Were we verify if the sum for making the cut is equal to the sum is equal to 40
            // I dont know if throw an exception, math should be manage from in the UI
            if ((_hDimension1 + _hDimension2 * 2 + _hDimension3) != _distanceBetweenRollers) return;

            // We create the the lines its a trapezoid

            Point firstPoint = new Point(-posx + _hDimension1 / 2, _positionY - _dFromRevolveToBottom);
            Point secondPoint = new Point(-posx + _hDimension1 / 2 + _hDimension2, -_dFromRevolveToBottom + _vDimension);


            // Here automaticaly is calculated the middle value

            double middleValue = _distanceBetweenRollers - (_hDimension1 + _hDimension2 * 2);

            Point thirdPoint = new Point(secondPoint.X * 1000.0 + middleValue, -_dFromRevolveToBottom + _vDimension);
            Point fourthPoint = new Point(thirdPoint.X * 1000.0 + _hDimension2, _positionY - _dFromRevolveToBottom);

            Point[] points = { firstPoint, secondPoint, thirdPoint, fourthPoint };


            for (int i = 0; i < points.Length - 1; i++)
            {
                sMan.CreateLine(points[i].X, points[i].Y, 0, points[i + 1].X, points[i + 1].Y, 0);
            }

            // UnionLine



            _model.InsertSketch2(true);

        }
    }

    public class Step
    {
        public double Length { get; private set; }
        public double Diameter { get; private set; }

        public Step(double diameter, double length)
        {
            Diameter = diameter;
            Length = length;
        }
    }

}
