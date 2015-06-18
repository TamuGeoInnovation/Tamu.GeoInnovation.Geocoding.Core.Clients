using System;
using System.Collections;
using System.Data.SqlClient;

using USC.GISResearchLab.Common.Addresses;
using USC.GISResearchLab.Common.Exceptions.Geocoding;
using USC.GISResearchLab.Common.Geometries.Points;
using USC.GISResearchLab.Geocoding.Core.Algorithms.FeatureInterpolationMethods;
using USC.GISResearchLab.Geocoding.Core.Algorithms.FeatureInterpolationMethods.Implementations;
using USC.GISResearchLab.Geocoding.Core.Algorithms.FeatureInterpolationMethods.Interfaces;
using USC.GISResearchLab.Geocoding.Core.InputData.Standardizing;
using USC.GISResearchLab.Geocoding.Core.Metadata;
using USC.GISResearchLab.Geocoding.Core.OutputData;
using USC.GISResearchLab.Geocoding.Core.Queries.Parameters;
using USC.GISResearchLab.Geocoding.Core.ReferenceDatasets.Sources;
using USC.GISResearchLab.Geocoding.Core.ReferenceDatasets.Sources.Implementations;
using USC.GISResearchLab.Geocoding.Core.ReferenceDatasets.Sources.Interfaces;
using USC.GISResearchLab.Geocoding.Core.Utils.Qualities;
using USC.GISResearchLab.Common.Geometries;
using USC.GISResearchLab.Common.Geocoders.ReferenceDatasets.Sources.Interfaces;
using USC.GISResearchLab.Geocoding.Scrapers.LAAssessor;
using USC.GISResearchLab.Common.Utils.Files;
using USC.GISResearchLab.Geocoding.Core.Metadata.Qualities;
using USC.GISResearchLab.Common.Core.Geocoders.GeocodingQueries;
using USC.GISResearchLab.Common.Core.Geocoders.GeocodingQueries.Options;
using USC.GISResearchLab.Common.Core.Geocoders.FeatureMatching;

namespace USC.GISResearchLab.Geocoding.Core
{
    public class Geocoder
    {
        #region Properties
        
        private LAAssessor _LAAssessor;
        public LAAssessor LAAssessor
        {
            get { return _LAAssessor; }
            set { _LAAssessor = value; }
        }

        private ArrayList _InterpolationMethods;
        public ArrayList InterpolationMethods
        {
            get { return _InterpolationMethods; }
            set { _InterpolationMethods = value; }
        }

        private Parcels _Parcels;
        public Parcels Parcels
        {
            get { return _Parcels; }
            set { _Parcels = value; }
        }

        private ParcelCentroids _ParcelCentroids;
        public ParcelCentroids ParcelCentroids
        {
            get { return _ParcelCentroids; }
            set { _ParcelCentroids = value; }
        }

        private Cities _Cities;
        public Cities Cities
        {
            get { return _Cities; }
            set { _Cities = value; }
        }

        private Counties _Counties;
        public Counties Counties
        {
            get { return _Counties; }
            set { _Counties = value; }
        }

        private CountySubregions _CountySubregions;
        public CountySubregions CountySubregions
        {
            get { return _CountySubregions; }
            set { _CountySubregions = value; }
        }

        private TigerLines _TigerLines;
        public TigerLines TigerLines
        {
            get { return _TigerLines; }
            set { _TigerLines = value; }
        }

        private ZipCode _ZIPCodes;
        public ZipCode ZIPCodes
        {
            get { return _ZIPCodes; }
            set { _ZIPCodes = value; }
        }

        private string _AgentServerURL;
        public string AgentServerURL
        {
            get { return _AgentServerURL; }
            set { _AgentServerURL = value; }
        }
        #endregion


        public Geocoder()
        {
        }

        public GeocodeResultSet Geocode(string streetAddress, string city, string state, string zip)
        {
            StreetAddress address;
            try
            {
                address = AddressStandardizer.normalizeAddress(streetAddress, city, state, zip);
            }
            catch (GeocodeException e)
            {
                throw new Exception("Error occurred standardizing address: " + streetAddress, e);
            }

            GeocodingQuery query = new GeocodingQuery(address, new BaseOptions());
            return Geocode(query);
        }

        public GeocodeResultSet Geocode(string numberStr, string preDirectional, string name, string suffix, string postDirectional, string city, string state, string zipStr)
        {
            StreetAddress streetAddress = new StreetAddress(numberStr, preDirectional, name, suffix, postDirectional, city, state, zipStr);
            return Geocode(streetAddress);
        }

        public GeocodeResultSet Geocode(string numberStr, string preDirectional, string name, string suffix, string postDirectional, string city, string state, string zipStr, BaseOptions baseOptions)
        {
            StreetAddress streetAddress = new StreetAddress(numberStr, preDirectional, name, suffix, postDirectional, city, state, zipStr);
            return Geocode(streetAddress, baseOptions);
        }

        public GeocodeResultSet Geocode(StreetAddress address)
        {
            return Geocode(address, new BaseOptions());
        }

        public GeocodeResultSet Geocode(StreetAddress address, BaseOptions baseOptions)
        {
            GeocodingQuery query = new GeocodingQuery(address, baseOptions);
            return Geocode(query);
        }

        public GeocodeResultSet Geocode(GeocodingQuery query)
        {
            GeocodeResultSet ret = new GeocodeResultSet();
            ret.StartTime = DateTime.Now;
            try
            {

                Geocode geocodeParcelCentroidPoint = PointGeocode(query, new ParcelCentroidPointInterplationMethod(ParcelCentroids));
                ret.AddGeocode(geocodeParcelCentroidPoint);

                // go on to testing more geocodes if 
                // 1) previous one was not valid
                // 2) the user specifically wants it
                // 3) using the uncertainty hierarchy - requires that all geocodes be created to test which has the smallest area (uncertainty)

                if (!geocodeParcelCentroidPoint.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                {
                    Geocode geocodeActualGeometry = PolygonGeocode(query, new ActualGeometryMethod(Parcels));
                    ret.AddGeocode(geocodeActualGeometry);

                    if (!geocodeActualGeometry.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                    {
                        //Geocode geocodeActualGeometry = PolygonGeocode(address, new ActualGeometryMethod(LAAssessor));

                        Geocode geocodeUniformLot = LinearGeocode(query, new UniformLot(TigerLines, LAAssessor, LAAssessor));
                        ret.AddGeocode(geocodeUniformLot);

                        if (!geocodeUniformLot.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                        {

                            Geocode geocodeAddressRange = LinearGeocode(query, new AddressRangeMethod(TigerLines));
                            ret.AddGeocode(geocodeAddressRange);

                            if (!geocodeAddressRange.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                            {

                                Geocode geocodeZIPCentroid = PointGeocode(query, new ZipCodeCentroid(ZIPCodes));
                                ret.AddGeocode(geocodeZIPCentroid);

                                if (!geocodeZIPCentroid.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                                {

                                    Geocode geocodeCityCentroid = PointGeocode(query, new CityCentroid(Cities));
                                    ret.AddGeocode(geocodeCityCentroid);

                                    if (!geocodeCityCentroid.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                                    {

                                        Geocode geocodeCountySubregion = PointGeocode(query, new CountySubregionCentroid(CountySubregions));
                                        ret.AddGeocode(geocodeCountySubregion);

                                        if (!geocodeCountySubregion.Valid || query.BaseOptions.ShouldReturnExhaustiveGeocodes || query.BaseOptions.FeatureMatchingHierarchy == FeatureMatchingHierarchy.UncertaintyBased)
                                        {
                                            Geocode geocodeCounty = PointGeocode(query, new CountyCentroid(Counties));
                                            ret.AddGeocode(geocodeCounty);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                ret.EndTime = DateTime.Now;
                ret.Statistics.EndTime = DateTime.Now;
                ret.TimeTaken = TimeSpan.FromTicks(ret.EndTime.Ticks - ret.StartTime.Ticks);

            }
            catch (Exception e)
            {
                ret.Resultstring = e.GetType() + ": " + e.Message;
                ret.Statistics.QualityStatistics.QualityIndex = (int)(geocodeQualityType.Unmatchable);
            }
            return ret;
        }

        public Geocode PointGeocode(GeocodingQuery query , IPointInterpolationMethod method)
        {
            Geocode ret = null;
            try
            {
                ret = new Geocode();
                ret.Statistics.StartTime = DateTime.Now;
                ParameterSet parameterSet = ParameterSet.buildParameterSet(query.StreetAddress);
                parameterSet.shouldUseSubstring = query.BaseOptions.ShouldUseSubstring;
                parameterSet.ShouldUseRelaxation = query.BaseOptions.ShouldUseRelaxation;
                parameterSet.ShouldUseCaching = query.BaseOptions.ShouldUseCaching;
                parameterSet.ShouldUseSoundex = query.BaseOptions.ShouldUseSoundex;
                parameterSet.RelaxableAttributes = query.BaseOptions.RelaxableAttributes;
                ret = method.Geocode(parameterSet);
                ret.Statistics.EndTime = DateTime.Now;
                ret.TimeTaken = TimeSpan.FromTicks(ret.Statistics.EndTime.Ticks - ret.Statistics.StartTime.Ticks);
            }
            catch (Exception e)
            {
                ret.GeocodedError.GeoError += e.GetType() + ": " + e.Message;
            }
            return ret;
        }


        public Geocode LinearGeocode(GeocodingQuery query, ILinearInterpolationMethod method)
        {
            Geocode ret = null;
            try
            {

              
                ret = new Geocode();
                ret.Statistics.StartTime = DateTime.Now;
                ParameterSet parameterSet = ParameterSet.buildParameterSet(query.StreetAddress);
                parameterSet.shouldUseSubstring = query.BaseOptions.ShouldUseSubstring;
                parameterSet.ShouldUseRelaxation = query.BaseOptions.ShouldUseRelaxation;
                parameterSet.ShouldUseCaching = query.BaseOptions.ShouldUseCaching;
                parameterSet.ShouldUseSoundex = query.BaseOptions.ShouldUseSoundex;
                parameterSet.RelaxableAttributes = query.BaseOptions.RelaxableAttributes;
                ret = method.Geocode(parameterSet);
                ret.Statistics.EndTime = DateTime.Now;
                ret.TimeTaken = TimeSpan.FromTicks(ret.Statistics.EndTime.Ticks - ret.Statistics.StartTime.Ticks);
            }
            catch (Exception e)
            {
                ret.GeocodedError.GeoError += e.GetType() + ": " + e.Message;
            }
            return ret;
        }

        public Geocode PolygonGeocode(GeocodingQuery query, IPolygonInterpolationMethod method)
        {
            Geocode ret = null;
            try
            {
                ret = new Geocode();
                ret.Statistics.StartTime = DateTime.Now;
                ParameterSet parameterSet = ParameterSet.buildParameterSet(query.StreetAddress);
                parameterSet.shouldUseSubstring = query.BaseOptions.ShouldUseSubstring;
                parameterSet.ShouldUseRelaxation = query.BaseOptions.ShouldUseRelaxation;
                parameterSet.ShouldUseCaching = query.BaseOptions.ShouldUseCaching;
                parameterSet.ShouldUseSoundex = query.BaseOptions.ShouldUseSoundex;
                parameterSet.RelaxableAttributes = query.BaseOptions.RelaxableAttributes;
                ret = method.Geocode(parameterSet);
                ret.Statistics.EndTime = DateTime.Now;
                ret.TimeTaken = TimeSpan.FromTicks(ret.Statistics.EndTime.Ticks - ret.Statistics.StartTime.Ticks);
            }
            catch (Exception e)
            {
                ret.GeocodedError.GeoError += e.GetType() + ": " + e.Message;
            }
            return ret;
        }



        //public Geocode Geocode(string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr, IInterpolationMethod method, IGeometrySource<Geometry> source)
        //{

        //    Geocode ret = null;
        //    try
        //    {
        //        ret = new Geocode();
        //        ret.Statistics.StartTime = DateTime.Now;
        //        ParameterSet parameterSet = ParameterSet.buildParameterSet(address);
        //        ret = method.geocode(parameterSet, source);
        //        ret.Statistics.EndTime = DateTime.Now;


        //        //// setup the appropriate source factory
        //        //SourceFactory sourceFactory = null;

        //        //switch (parameterSet.sourceInt)
        //        //{
        //        //    case DataSourceNames.SOURCE_TIGERLINES:
        //        //        sourceFactory = new TigerLinesFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_TIGERLINES_CONFLATED:
        //        //        sourceFactory = new TigerLinesConflatedFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_NAVTECH:
        //        //        sourceFactory = new NavTechFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_ZIP_CODE_CENTROIDS:
        //        //        sourceFactory = new ZipCodeFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_CITY_CENTROIDS:
        //        //        sourceFactory = new CitiesFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_COUNTY_SUBREGION_CENTROIDS:
        //        //        sourceFactory = new CountySubregionsFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_COUNTY_CENTROIDS:
        //        //        sourceFactory = new CountiesFactory();
        //        //        break;

        //        //    case DataSourceNames.SOURCE_STATE_CENTROIDS:
        //        //        sourceFactory = new StatesFactory();
        //        //        break;

        //        //    default:
        //        //        break;
        //        //}

        //        //Source dataSource;

        //        //if (sourceFactory != null)
        //        //{
        //        //    dataSource = sourceFactory.GetObject(dbServer, catalog, userName, password);
        //        //}
        //        //else
        //        //{
        //        //    throw new Exception("Unable to create source factory for source:" + parameterSet.sourceStr);
        //        //}

                
        //    }
        //    catch (Exception e)
        //    {
        //        ret.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //    }

        //    return ret;
        //}


        //public Geocode GeocodeAddressRange(string polyline, string fromAddressLeft, string toAddressLeft, string fromAddressRight, string toAddressRight, double roadWidth, string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr, string source)
        //{

        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode geoPoint = new Geocode();

        //    AddressRangeMethod addressRange = new AddressRangeMethod();

        //    try
        //    {
        //        geoPoint = addressRange.geocodeGivenSegment(polyline, fromAddressLeft.Trim(), toAddressLeft.Trim(), fromAddressRight.Trim(), toAddressRight.Trim(), roadWidth, numberStr.Trim());
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //    }

        //    geoPoint.Statistics.ParameterStatistics.Method = addressRange.GetName();
        //    geoPoint.Statistics.ParameterStatistics.Source = source;

        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    geoPoint.Statistics.TimeTaken = duration;
        //    //geoPoint.debug = geoPoint.ToString();

        //    return geoPoint;
        //}


        
        //public Geocode GeocodeUniformLot(string polyline, int lotNumber, double numberOfLotsLeft, double numberOfLotsRight, string fromAddressLeft, string toAddressLeft, string fromAddressRight, string toAddressRight, double roadWidth, string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr, string source)
        //{

        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode geoPoint = new Geocode();

        //    UniformLot uniformLot = new UniformLot();

        //    try
        //    {
        //        geoPoint = uniformLot.geocodeGivenSegmentAndNumberOfParcelsAndParcelNumber(polyline, fromAddressLeft.Trim(), toAddressLeft.Trim(), fromAddressRight.Trim(), toAddressRight.Trim(), numberOfLotsLeft, numberOfLotsRight, lotNumber, roadWidth, numberStr.Trim(), preDirectional.Trim(), name.Trim(), suffix.Trim(), postDirectional.Trim(), suite.Trim(), suiteNumberStr.Trim(), city.Trim(), state.Trim(), zipStr.Trim());
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //    }

        //    geoPoint.Statistics.ParameterStatistics.Method = uniformLot.GetName();
        //    geoPoint.Statistics.ParameterStatistics.Source = source;

        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    geoPoint.Statistics.TimeTaken = duration;
        //    //geoPoint.debug = geoPoint.ToString();

        //    return geoPoint;
        //}




        
        //public Geocode GeocodeActualLot(string polyline, string source, string sourceId, string fromAddressLeft, string toAddressLeft, string fromAddressRight, string toAddressRight, double roadWidth, string dimensionstring, string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr, SqlConnection conn)
        //{

        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode geoPoint = new Geocode();

        //    ActualLotMethod actualLot = new ActualLotMethod();

        //    string[] segments = dimensionstring.Split(';');
        //    if (segments.Length == 4)
        //    {

        //        string[] segment1Dimensionstring = segments[0].Split(':');
        //        string[] segment2Dimensionstring = segments[1].Split(':');
        //        string[] segment3Dimensionstring = segments[2].Split(':');
        //        string[] segment4Dimensionstring = segments[3].Split(':');

        //        string segment1Info = segment1Dimensionstring[0];
        //        string segment1addressDimensions = segment1Dimensionstring[1];
        //        string segment2Info = segment2Dimensionstring[0];
        //        string segment2addressDimensions = segment2Dimensionstring[1];
        //        string segment3Info = segment3Dimensionstring[0];
        //        string segment3addressDimensions = segment3Dimensionstring[1];
        //        string segment4Info = segment4Dimensionstring[0];
        //        string segment4addressDimensions = segment4Dimensionstring[1];

        //        try
        //        {
        //            geoPoint = actualLot.geocodeGivenSegmentAndNumberOfParcelsAndParcelNumberAndParcelSizes(polyline, source, sourceId, fromAddressLeft, toAddressLeft, fromAddressRight, toAddressRight, roadWidth, segment1Info, segment1addressDimensions, segment2Info, segment2addressDimensions, segment3Info, segment3addressDimensions, segment4Info, segment4addressDimensions, numberStr, preDirectional, name, suffix, postDirectional, suite, suiteNumberStr, city, state, zipStr, conn, geoPoint.Statistics);
        //        }
        //        catch (GeocodeException e)
        //        {
        //            geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //        }
        //    }
        //    else
        //    {
        //        geoPoint.GeocodedError.GeoError = "4 Segments are required";
        //    }

        //    geoPoint.Statistics.ParameterStatistics.Method = actualLot.Name;
        //    geoPoint.Statistics.ParameterStatistics.Source = source;

        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    geoPoint.Statistics.TimeTaken = duration;
        //    //geoPoint.debug = geoPoint.ToString();

        //    return geoPoint;
        //}


        //		
        //		public Geocode GeocodeActualGeometry( string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr, string source)
        //		{
        //		
        //			/* Read the initial time. */
        //			DateTime startTime = DateTime.Now;
        //
        //			Geocode geoPoint = new Geocode();
        //			
        //			UniformLot uniformLot = new UniformLot();
        //
        //			try
        //			{
        //				geoPoint = uniformLot.geocodeGivenSegmentAndNumberOfParcelsAnParcelNumber(polyline, fromAddressLeft.Trim(), toAddressLeft.Trim(), fromAddressRight.Trim(), toAddressRight.Trim(), numberOfLotsLeft, numberOfLotsRight, lotNumber, roadWidth, numberStr.Trim());
        //			}
        //			catch(GeocodeException e)
        //			{
        //				geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //			}
        //
        //			geoPoint.Statistics.ParameterStatistics.Method = uniformLot.getName();
        //			geoPoint.Statistics.ParameterStatistics.Source = source;
        //
        //			/* Read the end time. */
        //			DateTime stopTime = DateTime.Now;
        //			TimeSpan duration = stopTime - startTime;
        //
        //			geoPoint.Statistics.TimeTaken = duration;
        //			geoPoint.debug = geoPoint.ToString();
        //		
        //			return geoPoint;
        //		}
        //

        
        //public Geocode geocodeActualGeometry(string polyline, string source, int sourceId, string coordinates, double centroidX, double centroidY, string fromAddressLeft, string toAddressLeft, string fromAddressRight, string toAddressRight, double roadWidth, string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr)
        //{
        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode geoPoint = new Geocode();
        //    Geocode geoPointUniformLot = new Geocode();

        //    ParameterSet parameterSet;
        //    try
        //    {
        //        parameterSet = ParameterSet.buildParameterSet(numberStr, preDirectional, name, suffix, postDirectional, suite, suiteNumberStr, city, state, zipStr, "u", "t", null);
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //        return geoPoint;
        //    }

        //    UniformLot uniformLot = new UniformLot();

        //    try
        //    {
        //        geoPointUniformLot = uniformLot.geocode(parameterSet, new TigerLines());
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPointUniformLot.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //    }

        //    geoPoint.Statistics = geoPointUniformLot.Statistics;

        //    geoPoint.Statistics.ParameterStatistics.Method = IneterpolationMethodNames.METHOD_NAME_ACTUAL_GEOMETRY;
        //    geoPoint.Statistics.ParameterStatistics.Source = source;

        //    ((Point) geoPoint.Geometry).Y = centroidY;
        //    ((Point) geoPoint.Geometry).X = centroidX;

        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    geoPoint.Statistics.TimeTaken = duration;

        //    return geoPoint;
        //}


        
        //public Geocode geocodeActualGeometryNoMediator(string polyline, string source, int sourceId, string fromAddressLeft, string toAddressLeft, string fromAddressRight, string toAddressRight, string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr)
        //{
        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode geoPoint = new Geocode();

        //    ParameterSet parameterSet;
        //    // setup the parameters
        //    try
        //    {
        //        parameterSet = ParameterSet.buildParameterSet(numberStr, preDirectional, name, suffix, postDirectional, suite, suiteNumberStr, city, state, zipStr, "actual geometry", "t", null);
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //        return geoPoint;
        //    }



        //    ActualGeometryMethod actualGeometry = new ActualGeometryMethod();
        //    geoPoint = actualGeometry.geocode(parameterSet, new TigerLines());

        //    geoPoint.Statistics.ParameterStatistics.Method = actualGeometry.GetName();
        //    geoPoint.Statistics.ParameterStatistics.Source = source;

        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    geoPoint.Statistics.TimeTaken = duration;

        //    return geoPoint;
        //}

        
        


        
        //public Geocode GeocodeWithAgentURLParameter(string numberStr, string preDirectional, string name, string suffix, string postDirectional, string suite, string suiteNumberStr, string city, string state, string zipStr, string method, string source, string agentUrl)
        //{

        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode geoPoint = new Geocode();
        //    ParameterSet parameterSet;



        //    // setup the parameters
        //    try
        //    {
        //        parameterSet = ParameterSet.buildParameterSet(numberStr, preDirectional, name, suffix, postDirectional, suite, suiteNumberStr, city, state, zipStr, method, source, agentUrl);
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //        return geoPoint;
        //    }


        //    // refresh the agentUrl file
        //    // find and uncomment
        //    //FileUtils.refresh(LAAssessorUtils.AGENT_URL_FILE_PATH, agentUrl);


        //    // setup the appropriate source factory
        //    ISegmentSourceFactory sourceFactory = null;

        //    switch (parameterSet.sourceInt)
        //    {
        //        case DataSourceNames.SOURCE_TIGERLINES:
        //            sourceFactory = new TigerLinesFactory();
        //            break;

        //        case DataSourceNames.SOURCE_TIGERLINES_CONFLATED:
        //            sourceFactory = new TigerLinesConflatedFactory();
        //            break;

        //        case DataSourceNames.SOURCE_NAVTECH:
        //            sourceFactory = new NavTechFactory();
        //            break;

        //        default:
        //            break;
        //    }

        //    IGeometrySource dataSource;
        //    if (sourceFactory != null)
        //    {
        //        dataSource = sourceFactory.GetObject(dbServer, catalog, userName, password);
        //    }
        //    else
        //    {
        //        throw new Exception("Unable to create source factory for source:" + parameterSet.sourceStr);
        //    }

        //    // setup the appropriate method factory
        //    IInterpolationMethodFactory methodFactory = null;

        //    try
        //    {
        //        switch (parameterSet.methodInt)
        //        {
        //            case IneterpolationMethodNames.METHOD_ADDRESS_RANGE:
        //                methodFactory = new AddressRangeFactory();
        //                break;

        //            case IneterpolationMethodNames.METHOD_UNIFORM_LOT_SIZE:
        //                methodFactory = new UniformLotFactory();
        //                break;

        //            case IneterpolationMethodNames.METHOD_ACTUAL_LOT_SIZE:
        //                methodFactory = new ActualLotFactory();
        //                break;

        //            case IneterpolationMethodNames.METHOD_ACTUAL_GEOMETRY:
        //                methodFactory = new ActualGeometryFactory();
        //                break;

        //            default:
        //                break;
        //        }
        //    }
        //    catch (NotImplementedException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //        return geoPoint;
        //    }

        //    IInterpolationMethod geocodingMethod;
        //    if (methodFactory != null)
        //    {
        //        geocodingMethod = methodFactory.GetObject();
        //    }
        //    else
        //    {
        //        throw new Exception("Unable to create method factory for method:" + parameterSet.methodStr);
        //    }


        //    try
        //    {
        //        geoPoint = geocodingMethod.geocode(parameterSet, dataSource);
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //    }

        //    geoPoint.Statistics.ParameterStatistics.Method = geocodingMethod.GetName();
        //    geoPoint.Statistics.ParameterStatistics.Source = dataSource.Name;

        //    // remove the agentUrl file
        //    //FileUtils.remove(LAAssessorUtils.AGENT_URL_FILE_PATH);

        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    geoPoint.Statistics.TimeTaken = duration;
        //    //geoPoint.debug = geoPoint.ToString();

        //    return geoPoint;
        //}


        //public Geocode GeocodeWithAgentURLParameterAndAssessorId(string assessorId, SqlConnection conn, string agentUrl)
        //{

        //    /* Read the initial time. */
        //    DateTime startTime = DateTime.Now;

        //    Geocode ret = new Geocode();
        //    ActualGeometryMethod geocodingMethod = new ActualGeometryMethod();

        //    try
        //    {
        //        ret = geocodingMethod.getGeocode(assessorId, agentUrl, conn, ret.Statistics);
        //    }
        //    catch (GeocodeException e)
        //    {
        //        ret.GeocodedError.GeoError = e.GetType() + ": " + e.Message;
        //    }


        //    /* Read the end time. */
        //    DateTime stopTime = DateTime.Now;
        //    TimeSpan duration = stopTime - startTime;

        //    ret.Statistics.TimeTaken = duration;
        //    //geoPoint.debug = geoPoint.ToString();

        //    return ret;
        //}

        //public string getParcelCoodinatesForAddress(Address address)
        //{

        //    GeoPoint geoPoint = new GeoPoint();
        //    ParameterSet parameterSet = null;
        //    // setup the parameters
        //    try
        //    {
        //        parameterSet = ParameterSetUtils.buildParameterSet(address.numberStr, address.preDirectional, address.name, address.suffix, address.postDirectional, address.suite, address.suiteNumberStr, address.city, address.state, address.zipStr, "actual geometry", "t", null);
        //    }
        //    catch (GeocodeException e)
        //    {
        //        geoPoint.geoError = e.GetType() + ": " + e.Message;
        //        return geoPoint.geoError;
        //    }

        //    ActualGeometry actualGeometry = new ActualGeometry();
        //    geoPoint = actualGeometry.geocode(parameterSet, new TigerLines());

        //    return geoPoint.CoordinateString;

        //}

    }
}
