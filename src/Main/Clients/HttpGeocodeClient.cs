using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlTypes;
using System.IO;
using System.Threading;
using System.Xml;
using Microsoft.SqlServer.Types;
using TAMU.GeoInnovation.PointIntersectors.Census.OutputData.CensusRecords;
using USC.GISResearchLab.Census.Core.Configurations.ServerConfigurations;
using USC.GISResearchLab.Common.Addresses;
using USC.GISResearchLab.Common.Core.Geocoders.GeocodingQueries;
using USC.GISResearchLab.Common.Core.Geocoders.GeocodingQueries.Options;
using USC.GISResearchLab.Common.Utils.Encoding;
using USC.GISResearchLab.Common.Utils.Strings;
using USC.GISResearchLab.Geocoding.Core.Metadata.FeatureMatchingResults;
using USC.GISResearchLab.Geocoding.Core.Metadata.Qualities;
using USC.GISResearchLab.Geocoding.Core.OutputData;
using USC.GISResearchLab.Geocoding.Core.Runners.Databases;
using USC.GISResearchLab.Geocoding.Core.Configurations;
using USC.GISResearchLab.Common.Geometries.Points;
using USC.GISResearchLab.Core.WebServices.ResultCodes;
using USC.GISResearchLab.Geocoding.Core.Algorithms.FeatureInterpolationMethods;
using USC.GISResearchLab.Common.Geographics.Units.Linears;
using USC.GISResearchLab.Common.Geographics.Units;
using USC.GISResearchLab.Common.Core.Geocoders.FeatureMatching;
using USC.GISResearchLab.Geocoding.Core.Algorithms.TieHandlingMethods;
using USC.GISResearchLab.Common.Core.Utils.Web.WebRequests;

namespace USC.GISResearchLab.Geocoding.Core.Clients
{
    public class HttpGeocodeClient : NonParsedGeocoderDatabaseRunner
    {
        #region Properties

        #endregion

        public static GeocodeResultSet Geocode(GeocodingQuery query, GeocoderConfiguration geocoderConfiguration)
        {
            return ProcessRecord("", query.StreetAddress, query.BaseOptions, geocoderConfiguration);
        }

        public static GeocodeResultSet Geocode(StreetAddress address, BaseOptions baseOptions, GeocoderConfiguration geocoderConfiguration)
        {
            return ProcessRecord("", address, baseOptions, geocoderConfiguration);
        }

        public static GeocodeResultSet ProcessRecord(object recordId, object record, BaseOptions baseOptions, GeocoderConfiguration geocoderConfiguration)
        {
            GeocodeResultSet ret = new GeocodeResultSet();
            string added = DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss");
            DateTime timeStart = DateTime.Now;

            string webserviceResultString = "";

            try
            {

                StreetAddress streetAddress = (StreetAddress)record;
                bool shouldUseUncertainty = geocoderConfiguration.OutputHierarchyConfiguration.ShouldUseUncertaintyMethodSingleFeatureArea;
                string hierarchy = "";
                if (shouldUseUncertainty)
                {
                    hierarchy = "u";
                }
                else
                {
                    hierarchy = "h";
                }

                string shouldUseRelaxation = geocoderConfiguration.FeatureMatchingConfiguration.ShouldUseAttributeRelaxation.ToString();
                string shouldUseRelaxationAttributes = geocoderConfiguration.FeatureMatchingConfiguration.GetRelaxedAttributesAsCSV();
                string shouldUseSubstring = geocoderConfiguration.FeatureMatchingConfiguration.ShouldUseSubstringMatching.ToString();
                string shouldUseSoundex = geocoderConfiguration.FeatureMatchingConfiguration.ShouldUseSoundex.ToString();
                string shouldUseSoundexAttributes = geocoderConfiguration.FeatureMatchingConfiguration.GetSoundexAttributesAsCSV();
                string referenceSources = geocoderConfiguration.ReferenceDatasetConfiguration.GetReferenceSourcesShortString();
                string shouldNotStore = geocoderConfiguration.ServerConfiguration.ShouldNotStoreTransactionDetails.ToString();
                string shouldCalculateCensus = baseOptions.ShouldOutputCensusFields.ToString();

                NameValueCollection parameters = new NameValueCollection();
                parameters.Add("apiKey", geocoderConfiguration.ServerConfiguration.ApiKey);
                parameters.Add("version", geocoderConfiguration.Version.ToString());
                parameters.Add("streetAddress", WebEncodingUtils.URLEncode(streetAddress.GetStreetAddressPortionAsString()));
                parameters.Add("city", WebEncodingUtils.URLEncode(streetAddress.City));
                parameters.Add("state", WebEncodingUtils.URLEncode(streetAddress.State));

                if (String.IsNullOrEmpty(streetAddress.ZIPPlus4))
                {
                    parameters.Add("zip", WebEncodingUtils.URLEncode(streetAddress.ZIP));
                }
                else
                {
                    parameters.Add("zip", WebEncodingUtils.URLEncode(streetAddress.ZIP + "-" + streetAddress.ZIPPlus4));
                }

                parameters.Add("verbose", "true");
                parameters.Add("format", "csv");
                parameters.Add("r", shouldUseRelaxation);
                parameters.Add("ratts", shouldUseRelaxationAttributes);
                parameters.Add("sub", shouldUseSubstring);
                parameters.Add("sou", shouldUseSoundex);
                parameters.Add("souatts", shouldUseSoundexAttributes);
                parameters.Add("refs", referenceSources);
                parameters.Add("notStore", shouldNotStore);
                parameters.Add("h", hierarchy);
                parameters.Add("c", shouldCalculateCensus);

                int censusYear = -1;
                switch (geocoderConfiguration.CensusConfiguration.CensusYearConfiguration.CensusYear)
                {
                    case CensusYear.NineteenNinety:
                        censusYear = 1990;
                        break;
                    case CensusYear.TwoThousand:
                        censusYear = 2000;
                        break;
                    case CensusYear.TwoThousandTen:
                        censusYear = 2010;
                        break;
                }

                parameters.Add("censusYear", censusYear.ToString());

                webserviceResultString = WebRequestUtils.SendPostRequest(geocoderConfiguration.ServerConfiguration.ApiHttpUrl, "text/plain", parameters, null);

                if (webserviceResultString != null)
                {
                    List<IGeocode> geocodes = new List<IGeocode>();

                    int version = Convert.ToInt32(baseOptions.Version * 100.0);

                    switch (version)
                    {
                        case 295:
                            Geocode geocode1 = FromCsv_V2_95(streetAddress, webserviceResultString, baseOptions);
                            geocodes.Add(geocode1);
                            break;
                        case 296:
                            Geocode geocode2 = FromCsv_V2_96(streetAddress, webserviceResultString, baseOptions);
                            geocodes.Add(geocode2);
                            break;
                        case 301:
                            Geocode geocode3 = FromCsv_V3_01(streetAddress, webserviceResultString, baseOptions);
                            geocodes.Add(geocode3);
                            break;
                        case 401:
                            geocodes = FromCsv_V4_01(streetAddress, webserviceResultString, baseOptions);
                            break;
                        default:
                            throw new Exception("Unexpected or unimplemented version: " + baseOptions.Version);
                    }

                    DateTime timeEnd = DateTime.Now;
                    

                    foreach (Geocode geocode in geocodes)
                    {

                        geocode.InputAddress = streetAddress;
                        //geocode.GeocoderName = RunnerName;

                        ret.RecordId = (string)recordId;
                        //ret.GeocoderName = RunnerName;

                        geocode.TimeTaken = timeEnd - timeStart;

                        ret.AddGeocode(geocode);

                        ret.TimeTaken += geocode.TimeTaken;
                        ret.TransactionId = geocode.TransactionId;

                        ret.AddGeocode(geocode);
                    }
                }
                else
                {
                    throw new Exception("Null return value from web service");
                }

            }
            catch (ThreadAbortException te)
            {
                throw te;
            }
            catch (Exception e)
            {
                throw new Exception("Error occured processing record: " + recordId + " : " + e.Message, e);
            }
            return ret;

        }

        public static List<IGeocode> FromCsv_V4_01(StreetAddress streetAddress, string webserviceResultString, BaseOptions baseOptions)
        {
            List<IGeocode> ret = new List<IGeocode>();

            string[] lines = webserviceResultString.Split(new string[] {Environment.NewLine}, StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 0)
            {

                foreach (string line in lines)
                {

                    Geocode geocode = new Geocode(baseOptions.Version);

                    string[] parts = line.Split(',');
                    if (parts.Length == 150)
                    {
                        if (parts != null)
                        {
                            int baseIndex = 0;
                            geocode.InputAddress = streetAddress;

                            geocode.TransactionId = parts[0];
                            geocode.Version = Convert.ToDouble(parts[1]);
                            geocode.SetQueryStatusCode(Convert.ToInt32(parts[2]));

                            if (geocode.QueryStatusCodes == QueryStatusCodes.Success)
                            {
                                geocode.Valid = true;
                            }

                            geocode.Geometry = new Point(Convert.ToDouble(parts[4]), Convert.ToDouble(parts[3]));

                            geocode.NAACCRGISCoordinateQualityCode = parts[5];
                            geocode.NAACCRGISCoordinateQualityName = parts[6];
                            geocode.NAACCRGISCoordinateQualityType = NAACCRGISCoordinateQuality.GetNAACCRGISCoordinateQualityTypeFromCode(geocode.NAACCRGISCoordinateQualityCode);

                            geocode.MatchScore = Convert.ToDouble(parts[7]);
                            //geocode.GeocodeQualityType = (GeocodeQualityType) Convert.ToInt32(parts[6]);
                            geocode.SetMatchType(parts[8]);
                            geocode.FM_GeographyType = (FeatureMatchingGeographyType)Enum.Parse(typeof(FeatureMatchingGeographyType), parts[9]);

                            geocode.RegionSize = parts[10];
                            geocode.RegionSizeUnits = parts[11];

                            geocode.InterpolationType = (InterpolationType)Enum.Parse(typeof(InterpolationType), parts[12]);
                            geocode.InterpolationSubType = (InterpolationSubType)Enum.Parse(typeof(InterpolationSubType), parts[13]);
                            geocode.SetMatchedLocationType(parts[14]);
                            geocode.FM_ResultType = (FeatureMatchingResultType)Enum.Parse(typeof(FeatureMatchingResultType), parts[15]);

                            if (!String.IsNullOrEmpty(parts[16]))
                            {
                                if (StringUtils.IsDouble(parts[16]))
                                {
                                    geocode.FM_ResultCount = Convert.ToInt32(parts[16]);
                                }
                            }

                            geocode.FM_Notes = parts[17];
                            geocode.FM_TieNotes = parts[18];
                            geocode.FM_TieStrategy = (TieHandlingStrategyType)Enum.Parse(typeof(TieHandlingStrategyType), parts[19]);
                            geocode.FM_SelectionMethod = (FeatureMatchingSelectionMethod)Enum.Parse(typeof(FeatureMatchingSelectionMethod), parts[20]);
                            geocode.FM_SelectionNotes = parts[21];

                            geocode.TimeTaken = TimeSpan.FromSeconds(Convert.ToDouble(parts[22]));

                            baseIndex = 23;
                            geocode.MatchedAddress = new RelaxableStreetAddress();
                            geocode.MatchedAddress.Number = parts[baseIndex++];
                            geocode.MatchedAddress.NumberFractional = parts[baseIndex++];
                            geocode.MatchedAddress.PreDirectional = parts[baseIndex++];
                            geocode.MatchedAddress.PreQualifier = parts[baseIndex++];
                            geocode.MatchedAddress.PreType = parts[baseIndex++];
                            geocode.MatchedAddress.PreArticle = parts[baseIndex++];
                            geocode.MatchedAddress.StreetName = parts[baseIndex++];
                            geocode.MatchedAddress.PostArticle = parts[baseIndex++];
                            geocode.MatchedAddress.PostQualifier = parts[baseIndex++];
                            geocode.MatchedAddress.Suffix = parts[baseIndex++];
                            geocode.MatchedAddress.PostDirectional = parts[baseIndex++];
                            geocode.MatchedAddress.SuiteType = parts[baseIndex++];
                            geocode.MatchedAddress.SuiteNumber = parts[baseIndex++];
                            geocode.MatchedAddress.PostOfficeBoxType = parts[baseIndex++];
                            geocode.MatchedAddress.PostOfficeBoxNumber = parts[baseIndex++];
                            geocode.MatchedAddress.City = parts[baseIndex++];
                            geocode.MatchedAddress.ConsolidatedCity = parts[baseIndex++];
                            geocode.MatchedAddress.MinorCivilDivision = parts[baseIndex++];
                            geocode.MatchedAddress.CountySubregion = parts[baseIndex++];
                            geocode.MatchedAddress.County = parts[baseIndex++];
                            geocode.MatchedAddress.State = parts[baseIndex++];
                            geocode.MatchedAddress.ZIP = parts[baseIndex++];
                            geocode.MatchedAddress.ZIPPlus1 = parts[baseIndex++];
                            geocode.MatchedAddress.ZIPPlus2 = parts[baseIndex++];
                            geocode.MatchedAddress.ZIPPlus3 = parts[baseIndex++];
                            geocode.MatchedAddress.ZIPPlus4 = parts[baseIndex++];
                            geocode.MatchedAddress.ZIPPlus5 = parts[baseIndex++];

                            baseIndex = 50;
                            geocode.ParsedAddress = new StreetAddress();
                            geocode.ParsedAddress.Number = parts[baseIndex++];
                            geocode.ParsedAddress.NumberFractional = parts[baseIndex++];
                            geocode.ParsedAddress.PreDirectional = parts[baseIndex++];
                            geocode.ParsedAddress.PreQualifier = parts[baseIndex++];
                            geocode.ParsedAddress.PreType = parts[baseIndex++];
                            geocode.ParsedAddress.PreArticle = parts[baseIndex++];
                            geocode.ParsedAddress.StreetName = parts[baseIndex++];
                            geocode.ParsedAddress.PostArticle = parts[baseIndex++];
                            geocode.ParsedAddress.PostQualifier = parts[baseIndex++];
                            geocode.ParsedAddress.Suffix = parts[baseIndex++];
                            geocode.ParsedAddress.PostDirectional = parts[baseIndex++];
                            geocode.ParsedAddress.SuiteType = parts[baseIndex++];
                            geocode.ParsedAddress.SuiteNumber = parts[baseIndex++];
                            geocode.ParsedAddress.PostOfficeBoxType = parts[baseIndex++];
                            geocode.ParsedAddress.PostOfficeBoxNumber = parts[baseIndex++];
                            geocode.ParsedAddress.City = parts[baseIndex++];
                            geocode.ParsedAddress.ConsolidatedCity = parts[baseIndex++];
                            geocode.ParsedAddress.MinorCivilDivision = parts[baseIndex++];
                            geocode.ParsedAddress.CountySubregion = parts[baseIndex++];
                            geocode.ParsedAddress.County = parts[baseIndex++];
                            geocode.ParsedAddress.State = parts[baseIndex++];
                            geocode.ParsedAddress.ZIP = parts[baseIndex++];
                            geocode.ParsedAddress.ZIPPlus1 = parts[baseIndex++];
                            geocode.ParsedAddress.ZIPPlus2 = parts[baseIndex++];
                            geocode.ParsedAddress.ZIPPlus3 = parts[baseIndex++];
                            geocode.ParsedAddress.ZIPPlus4 = parts[baseIndex++];
                            geocode.ParsedAddress.ZIPPlus5 = parts[baseIndex++];

                            baseIndex = 77;
                            geocode.MatchedFeatureAddress = new StreetAddress();
                            geocode.MatchedFeatureAddress.Number = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.NumberFractional = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PreDirectional = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PreQualifier = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PreType = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PreArticle = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.StreetName = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PostArticle = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PostQualifier = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.Suffix = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PostDirectional = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.SuiteType = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.SuiteNumber = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PostOfficeBoxType = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.PostOfficeBoxNumber = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.City = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ConsolidatedCity = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.MinorCivilDivision = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.CountySubregion = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.County = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.State = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ZIP = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ZIPPlus1 = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ZIPPlus2 = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ZIPPlus3 = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ZIPPlus4 = parts[baseIndex++];
                            geocode.MatchedFeatureAddress.ZIPPlus5 = parts[baseIndex++];


                            baseIndex = 104;
                            if (!String.IsNullOrEmpty(parts[104]))
                            {
                                if (StringUtils.IsDouble(parts[104]))
                                {
                                    geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Area = Convert.ToDouble(parts[104]);
                                    geocode.GeocodedError.ErrorBounds = Convert.ToDouble(parts[104]);
                                }
                            }

                            if (!String.IsNullOrEmpty(parts[105]))
                            {
                                if (UnitManager.IsUint(parts[105]))
                                {
                                    Unit unit = UnitManager.FromString(parts[105]);
                                    if (unit.UnitType == UnitTypes.Linear)
                                    {
                                        geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.AreaUnits = ((LinearUnit)unit).LinearUnitTypes;
                                        geocode.GeocodedError.ErrorBoundsUnit = ((LinearUnit)unit).LinearUnitTypes;
                                    }
                                }
                            }


                            string srid = parts[106];
                            if (!String.IsNullOrEmpty(srid))
                            {
                                geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID = Convert.ToInt32(srid);
                            }

                            string featureGml = parts[107];

                            if (!String.IsNullOrEmpty(featureGml))
                            {
                                try
                                {
                                    StringReader stringReader = new StringReader(featureGml);
                                    XmlReader xmlReader = XmlReader.Create(stringReader);
                                    SqlXml sqlXml = new SqlXml(xmlReader);
                                    geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry = SqlGeometry.GeomFromGml(sqlXml, geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID);

                                    if (geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry != null && !geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry.IsNull)
                                    {
                                        geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeography = SQLSpatialTools.SQLSpatialToolsFunctions.MakeValidGeographyFromGeometry(geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Error = ex.Message;
                                }
                            }

                            geocode.SourceType = parts[108];
                            geocode.SourceVintage = parts[109];

                            geocode.MatchedFeature.PrimaryIdField = parts[110];
                            geocode.MatchedFeature.PrimaryIdValue = parts[111];
                            geocode.MatchedFeature.SecondaryIdField = parts[112];
                            geocode.MatchedFeature.SecondaryIdValue = parts[113];

                            geocode.NAACCRCensusTractCertaintyCode = parts[114];
                            geocode.NAACCRCensusTractCertaintyName = parts[115];
                            geocode.NAACCRCensusTractCertaintyType = NAACCRCensusTractCertainty.GetNAACCRNAACCRCensusTractCertaintyTypeFromCode(geocode.NAACCRCensusTractCertaintyCode);

                            geocode.CensusRecords = new List<CensusOutputRecord>();

                            CensusOutputRecord censusRecord1990 = new CensusOutputRecord();
                            censusRecord1990.CensusYear = CensusYear.NineteenNinety;
                            censusRecord1990.CensusBlock = parts[116];
                            censusRecord1990.CensusBlockGroup = parts[117];
                            censusRecord1990.CensusTract = parts[118];
                            censusRecord1990.CensusCountyFips = parts[119];
                            censusRecord1990.CensusCbsaFips = parts[120];
                            censusRecord1990.CensusCbsaMicro = parts[121];
                            censusRecord1990.CensusMcdFips = parts[122];
                            censusRecord1990.CensusMetDivFips = parts[123];
                            censusRecord1990.CensusMsaFips = parts[124];
                            censusRecord1990.CensusPlaceFips = parts[125];
                            censusRecord1990.CensusStateFips = parts[126];

                            if (censusRecord1990.IsValid())
                            {
                                geocode.CensusRecords.Add(censusRecord1990);
                                geocode.CensusYear = censusRecord1990.CensusYear;
                                geocode.CensusBlock = censusRecord1990.CensusBlock;
                                geocode.CensusBlockGroup = censusRecord1990.CensusBlockGroup;
                                geocode.CensusTract = censusRecord1990.CensusTract;
                                geocode.CensusCountyFips = censusRecord1990.CensusCountyFips;
                                geocode.CensusCbsaFips = censusRecord1990.CensusCbsaFips;
                                geocode.CensusCbsaMicro = censusRecord1990.CensusCbsaMicro;
                                geocode.CensusMcdFips = censusRecord1990.CensusMcdFips;
                                geocode.CensusMetDivFips = censusRecord1990.CensusMetDivFips;
                                geocode.CensusMsaFips = censusRecord1990.CensusMsaFips;
                                geocode.CensusPlaceFips = censusRecord1990.CensusPlaceFips;
                                geocode.CensusStateFips = censusRecord1990.CensusStateFips;
                            }

                            CensusOutputRecord censusRecord2000 = new CensusOutputRecord();
                            censusRecord2000.CensusYear = CensusYear.TwoThousand;
                            censusRecord2000.CensusBlock = parts[127];
                            censusRecord2000.CensusBlockGroup = parts[128];
                            censusRecord2000.CensusTract = parts[129];
                            censusRecord2000.CensusCountyFips = parts[130];
                            censusRecord2000.CensusCbsaFips = parts[131];
                            censusRecord2000.CensusCbsaMicro = parts[132];
                            censusRecord2000.CensusMcdFips = parts[133];
                            censusRecord2000.CensusMetDivFips = parts[134];
                            censusRecord2000.CensusMsaFips = parts[135];
                            censusRecord2000.CensusPlaceFips = parts[136];
                            censusRecord2000.CensusStateFips = parts[137];

                            if (censusRecord2000.IsValid())
                            {
                                geocode.CensusRecords.Add(censusRecord2000);
                                geocode.CensusYear = censusRecord2000.CensusYear;
                                geocode.CensusBlock = censusRecord2000.CensusBlock;
                                geocode.CensusBlockGroup = censusRecord2000.CensusBlockGroup;
                                geocode.CensusTract = censusRecord2000.CensusTract;
                                geocode.CensusCountyFips = censusRecord2000.CensusCountyFips;
                                geocode.CensusCbsaFips = censusRecord2000.CensusCbsaFips;
                                geocode.CensusCbsaMicro = censusRecord2000.CensusCbsaMicro;
                                geocode.CensusMcdFips = censusRecord2000.CensusMcdFips;
                                geocode.CensusMetDivFips = censusRecord2000.CensusMetDivFips;
                                geocode.CensusMsaFips = censusRecord2000.CensusMsaFips;
                                geocode.CensusPlaceFips = censusRecord2000.CensusPlaceFips;
                                geocode.CensusStateFips = censusRecord2000.CensusStateFips;
                            }

                            CensusOutputRecord censusRecord2010 = new CensusOutputRecord();
                            censusRecord2010.CensusYear = CensusYear.TwoThousandTen;
                            censusRecord2010.CensusBlock = parts[138];
                            censusRecord2010.CensusBlockGroup = parts[139];
                            censusRecord2010.CensusTract = parts[140];
                            censusRecord2010.CensusCountyFips = parts[141];
                            censusRecord2010.CensusCbsaFips = parts[142];
                            censusRecord2010.CensusCbsaMicro = parts[143];
                            censusRecord2010.CensusMcdFips = parts[144];
                            censusRecord2010.CensusMetDivFips = parts[145];
                            censusRecord2010.CensusMsaFips = parts[146];
                            censusRecord2010.CensusPlaceFips = parts[147];
                            censusRecord2010.CensusStateFips = parts[148];

                            if (censusRecord2010.IsValid())
                            {
                                geocode.CensusRecords.Add(censusRecord2010);
                                geocode.CensusYear = censusRecord2010.CensusYear;
                                geocode.CensusBlock = censusRecord2010.CensusBlock;
                                geocode.CensusBlockGroup = censusRecord2010.CensusBlockGroup;
                                geocode.CensusTract = censusRecord2010.CensusTract;
                                geocode.CensusCountyFips = censusRecord2010.CensusCountyFips;
                                geocode.CensusCbsaFips = censusRecord2010.CensusCbsaFips;
                                geocode.CensusCbsaMicro = censusRecord2010.CensusCbsaMicro;
                                geocode.CensusMcdFips = censusRecord2010.CensusMcdFips;
                                geocode.CensusMetDivFips = censusRecord2010.CensusMetDivFips;
                                geocode.CensusMsaFips = censusRecord2010.CensusMsaFips;
                                geocode.CensusPlaceFips = censusRecord2010.CensusPlaceFips;
                                geocode.CensusStateFips = censusRecord2010.CensusStateFips;
                            }

                            ret.Add(geocode);

                        }
                    }
                    else
                    {
                        throw new Exception("Invalid return value from web service: " + webserviceResultString);
                    }
                }
            }
            else
            {
                throw new Exception("Invalid return value from web service: " + webserviceResultString);
            }

            return ret;
        }

        public static Geocode FromCsv_V3_01(StreetAddress streetAddress, string webserviceResultString, BaseOptions baseOptions)
        {
            Geocode geocode = new Geocode(baseOptions.Version);

            string[] parts = webserviceResultString.Split(',');
            if (parts.Length == 128)
            {
                if (parts != null)
                {
                    int baseIndex = 0;
                    geocode.InputAddress = streetAddress;

                    geocode.TransactionId = parts[0];
                    geocode.Version = Convert.ToDouble(parts[1]);
                    geocode.SetQueryStatusCode(Convert.ToInt32(parts[2]));

                    if (geocode.QueryStatusCodes == QueryStatusCodes.Success)
                    {
                        geocode.Valid = true;
                    }

                    geocode.Geometry = new Point(Convert.ToDouble(parts[4]), Convert.ToDouble(parts[3]));

                    geocode.NAACCRGISCoordinateQualityCode= parts[5];
                    geocode.NAACCRGISCoordinateQualityName = parts[6];
                    geocode.NAACCRGISCoordinateQualityType = NAACCRGISCoordinateQuality.GetNAACCRGISCoordinateQualityTypeFromCode(geocode.NAACCRGISCoordinateQualityCode);

                    geocode.MatchScore = Convert.ToDouble(parts[7]);
                    //geocode.GeocodeQualityType = (GeocodeQualityType) Convert.ToInt32(parts[6]);
                    geocode.SetMatchType(parts[8]);
                    geocode.FM_GeographyType = (FeatureMatchingGeographyType)Enum.Parse(typeof(FeatureMatchingGeographyType), parts[9]);

                    geocode.RegionSize = parts[10];
                    geocode.RegionSizeUnits = parts[11];

                    geocode.InterpolationType = (InterpolationType)Enum.Parse(typeof(InterpolationType), parts[12]);
                    geocode.InterpolationSubType = (InterpolationSubType)Enum.Parse(typeof(InterpolationSubType), parts[13]);
                    geocode.SetMatchedLocationType(parts[14]);
                    geocode.FM_ResultType = (FeatureMatchingResultType)Enum.Parse(typeof(FeatureMatchingResultType), parts[15]);

                    if (!String.IsNullOrEmpty(parts[16]))
                    {
                        if (StringUtils.IsDouble(parts[16]))
                        {
                            geocode.FM_ResultCount = Convert.ToInt32(parts[16]);
                        }
                    }

                    geocode.FM_Notes = parts[17];
                    geocode.FM_TieNotes = parts[18];
                    geocode.FM_TieStrategy = (TieHandlingStrategyType)Enum.Parse(typeof(TieHandlingStrategyType), parts[19]);
                    geocode.FM_SelectionMethod = (FeatureMatchingSelectionMethod)Enum.Parse(typeof(FeatureMatchingSelectionMethod), parts[20]);
                    geocode.FM_SelectionNotes = parts[21];

                    geocode.TimeTaken = TimeSpan.FromSeconds(Convert.ToDouble(parts[22]));

                    if (!String.IsNullOrEmpty(parts[23]))
                    {
                        geocode.CensusYear = (CensusYear)Enum.Parse(typeof(CensusYear), parts[23]);
                    }

                    geocode.NAACCRCensusTractCertaintyCode= parts[24];
                    geocode.NAACCRCensusTractCertaintyName = parts[25];
                    geocode.NAACCRCensusTractCertaintyType = NAACCRCensusTractCertainty.GetNAACCRNAACCRCensusTractCertaintyTypeFromCode(geocode.NAACCRCensusTractCertaintyCode);


                    geocode.CensusBlock = parts[26];
                    geocode.CensusBlockGroup = parts[27];
                    geocode.CensusTract = parts[28];
                    geocode.CensusCountyFips = parts[29];
                    geocode.CensusCbsaFips = parts[30];
                    geocode.CensusCbsaMicro = parts[31];
                    geocode.CensusMcdFips = parts[32];
                    geocode.CensusMetDivFips = parts[33];
                    geocode.CensusMsaFips = parts[34];
                    geocode.CensusPlaceFips = parts[35];
                    geocode.CensusStateFips = parts[36];

                    baseIndex = 37;
                    geocode.MatchedAddress = new RelaxableStreetAddress();
                    geocode.MatchedAddress.Number = parts[baseIndex++];
                    geocode.MatchedAddress.NumberFractional = parts[baseIndex++];
                    geocode.MatchedAddress.PreDirectional = parts[baseIndex++];
                    geocode.MatchedAddress.PreQualifier = parts[baseIndex++];
                    geocode.MatchedAddress.PreType = parts[baseIndex++];
                    geocode.MatchedAddress.PreArticle = parts[baseIndex++];
                    geocode.MatchedAddress.StreetName = parts[baseIndex++];
                    geocode.MatchedAddress.PostArticle = parts[baseIndex++];
                    geocode.MatchedAddress.PostQualifier = parts[baseIndex++];
                    geocode.MatchedAddress.Suffix = parts[baseIndex++];
                    geocode.MatchedAddress.PostDirectional = parts[baseIndex++];
                    geocode.MatchedAddress.SuiteType = parts[baseIndex++];
                    geocode.MatchedAddress.SuiteNumber = parts[baseIndex++];
                    geocode.MatchedAddress.PostOfficeBoxType = parts[baseIndex++];
                    geocode.MatchedAddress.PostOfficeBoxNumber = parts[baseIndex++];
                    geocode.MatchedAddress.City = parts[baseIndex++];
                    geocode.MatchedAddress.ConsolidatedCity = parts[baseIndex++];
                    geocode.MatchedAddress.MinorCivilDivision = parts[baseIndex++];
                    geocode.MatchedAddress.CountySubregion = parts[baseIndex++];
                    geocode.MatchedAddress.County = parts[baseIndex++];
                    geocode.MatchedAddress.State = parts[baseIndex++];
                    geocode.MatchedAddress.ZIP = parts[baseIndex++];
                    geocode.MatchedAddress.ZIPPlus1 = parts[baseIndex++];
                    geocode.MatchedAddress.ZIPPlus2 = parts[baseIndex++];
                    geocode.MatchedAddress.ZIPPlus3 = parts[baseIndex++];
                    geocode.MatchedAddress.ZIPPlus4 = parts[baseIndex++];
                    geocode.MatchedAddress.ZIPPlus5 = parts[baseIndex++];

                    baseIndex = 64;
                    geocode.ParsedAddress = new StreetAddress();
                    geocode.ParsedAddress.Number = parts[baseIndex++];
                    geocode.ParsedAddress.NumberFractional = parts[baseIndex++];
                    geocode.ParsedAddress.PreDirectional = parts[baseIndex++];
                    geocode.ParsedAddress.PreQualifier = parts[baseIndex++];
                    geocode.ParsedAddress.PreType = parts[baseIndex++];
                    geocode.ParsedAddress.PreArticle = parts[baseIndex++];
                    geocode.ParsedAddress.StreetName = parts[baseIndex++];
                    geocode.ParsedAddress.PostArticle = parts[baseIndex++];
                    geocode.ParsedAddress.PostQualifier = parts[baseIndex++];
                    geocode.ParsedAddress.Suffix = parts[baseIndex++];
                    geocode.ParsedAddress.PostDirectional = parts[baseIndex++];
                    geocode.ParsedAddress.SuiteType = parts[baseIndex++];
                    geocode.ParsedAddress.SuiteNumber = parts[baseIndex++];
                    geocode.ParsedAddress.PostOfficeBoxType = parts[baseIndex++];
                    geocode.ParsedAddress.PostOfficeBoxNumber = parts[baseIndex++];
                    geocode.ParsedAddress.City = parts[baseIndex++];
                    geocode.ParsedAddress.ConsolidatedCity = parts[baseIndex++];
                    geocode.ParsedAddress.MinorCivilDivision = parts[baseIndex++];
                    geocode.ParsedAddress.CountySubregion = parts[baseIndex++];
                    geocode.ParsedAddress.County = parts[baseIndex++];
                    geocode.ParsedAddress.State = parts[baseIndex++];
                    geocode.ParsedAddress.ZIP = parts[baseIndex++];
                    geocode.ParsedAddress.ZIPPlus1 = parts[baseIndex++];
                    geocode.ParsedAddress.ZIPPlus2 = parts[baseIndex++];
                    geocode.ParsedAddress.ZIPPlus3 = parts[baseIndex++];
                    geocode.ParsedAddress.ZIPPlus4 = parts[baseIndex++];
                    geocode.ParsedAddress.ZIPPlus5 = parts[baseIndex++];

                    baseIndex = 91;
                    geocode.MatchedFeatureAddress = new StreetAddress();
                    geocode.MatchedFeatureAddress.Number = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.NumberFractional = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PreDirectional = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PreQualifier = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PreType = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PreArticle = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.StreetName = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PostArticle = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PostQualifier = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.Suffix = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PostDirectional = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.SuiteType = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.SuiteNumber = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PostOfficeBoxType = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.PostOfficeBoxNumber = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.City = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ConsolidatedCity = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.MinorCivilDivision = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.CountySubregion = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.County = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.State = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ZIP = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ZIPPlus1 = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ZIPPlus2 = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ZIPPlus3 = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ZIPPlus4 = parts[baseIndex++];
                    geocode.MatchedFeatureAddress.ZIPPlus5 = parts[baseIndex++];


                    baseIndex = 118;
                    if (!String.IsNullOrEmpty(parts[118]))
                    {
                        if (StringUtils.IsDouble(parts[118]))
                        {
                            geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Area = Convert.ToDouble(parts[118]);
                            geocode.GeocodedError.ErrorBounds = Convert.ToDouble(parts[118]);
                        }
                    }

                    if (!String.IsNullOrEmpty(parts[119]))
                    {
                        if (UnitManager.IsUint(parts[119]))
                        {
                            Unit unit = UnitManager.FromString(parts[119]);
                            if (unit.UnitType == UnitTypes.Linear)
                            {
                                geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.AreaUnits = ((LinearUnit)unit).LinearUnitTypes;
                                geocode.GeocodedError.ErrorBoundsUnit = ((LinearUnit)unit).LinearUnitTypes;
                            }
                        }
                    }


                    string srid = parts[120];
                    if (!String.IsNullOrEmpty(srid))
                    {
                        geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID = Convert.ToInt32(srid);
                    }

                    string featureGml = parts[121];

                    if (!String.IsNullOrEmpty(featureGml))
                    {
                        try
                        {
                            StringReader stringReader = new StringReader(featureGml);
                            XmlReader xmlReader = XmlReader.Create(stringReader);
                            SqlXml sqlXml = new SqlXml(xmlReader);
                            geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry = SqlGeometry.GeomFromGml(sqlXml, geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID);

                            if (geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry != null && !geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry.IsNull)
                            {
                                geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeography = SQLSpatialTools.SQLSpatialToolsFunctions.MakeValidGeographyFromGeometry(geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry);
                            }
                        }
                        catch (Exception ex)
                        {
                            geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Error = ex.Message;
                        }
                    }

                    geocode.SourceType = parts[122];
                    geocode.SourceVintage = parts[123];

                    geocode.MatchedFeature.PrimaryIdField = parts[124];
                    geocode.MatchedFeature.PrimaryIdValue = parts[125];
                    geocode.MatchedFeature.SecondaryIdField = parts[126];
                    geocode.MatchedFeature.SecondaryIdValue = parts[127];
                }
            }
            else
            {
                throw new Exception("Invalid return value from web service: " + webserviceResultString);
            }

            return geocode;
        }

        public static Geocode FromCsv_V2_96(StreetAddress streetAddress, string webserviceResultString, BaseOptions baseOptions)
        {
            Geocode geocode = new Geocode(baseOptions.Version);

             string[] parts = webserviceResultString.Split(',');
             if (parts.Length == 122)
             {
                 if (parts != null)
                 {
                     int index = 0;
                     geocode.InputAddress = streetAddress;

                     geocode.TransactionId = parts[0];
                     geocode.Version = Convert.ToDouble(parts[1]);
                     geocode.SetQueryStatusCode(Convert.ToInt32(parts[2]));

                     if (geocode.QueryStatusCodes == QueryStatusCodes.Success)
                     {
                         geocode.Valid = true;
                     }

                     geocode.Geometry = new Point(Convert.ToDouble(parts[4]), Convert.ToDouble(parts[3]));
                     geocode.MatchScore = Convert.ToDouble(parts[5]);
                     //geocode.GeocodeQualityType = (GeocodeQualityType) Convert.ToInt32(parts[6]);
                     geocode.SetMatchType(parts[6]);
                     geocode.FM_GeographyType = (FeatureMatchingGeographyType)Enum.Parse(typeof(FeatureMatchingGeographyType), parts[7]);
                     geocode.InterpolationType = (InterpolationType)Enum.Parse(typeof(InterpolationType), parts[8]);
                     geocode.InterpolationSubType = (InterpolationSubType)Enum.Parse(typeof(InterpolationSubType), parts[9]);
                     geocode.SetMatchedLocationType(parts[10]);
                     geocode.FM_ResultType = (FeatureMatchingResultType)Enum.Parse(typeof(FeatureMatchingResultType), parts[11]);

                     if (!String.IsNullOrEmpty (parts[12]))
                     {
                         if (StringUtils.IsDouble(parts[12]))
                         {
                             geocode.FM_ResultCount = Convert.ToInt32(parts[12]);
                         }
                     }

                     geocode.FM_Notes = parts[13];
                     geocode.FM_TieNotes= parts[14];
                     geocode.FM_TieStrategy= (TieHandlingStrategyType)Enum.Parse(typeof(TieHandlingStrategyType), parts[15]);
                     geocode.FM_SelectionMethod = (FeatureMatchingSelectionMethod)Enum.Parse(typeof(FeatureMatchingSelectionMethod), parts[16]);
                     geocode.FM_SelectionNotes = parts[17];

                     geocode.TimeTaken = TimeSpan.FromSeconds(Convert.ToDouble(parts[18]));

                     if (!String.IsNullOrEmpty(parts[13]))
                     {
                         geocode.CensusYear = (CensusYear)Enum.Parse(typeof(CensusYear), parts[19]);
                     }
                     geocode.CensusBlock = parts[20];
                     geocode.CensusBlockGroup = parts[21];
                     geocode.CensusTract = parts[22];
                     geocode.CensusCountyFips = parts[23];
                     geocode.CensusCbsaFips = parts[24];
                     geocode.CensusCbsaMicro = parts[25];
                     geocode.CensusMcdFips = parts[26];
                     geocode.CensusMetDivFips = parts[27];
                     geocode.CensusMsaFips = parts[28];
                     geocode.CensusPlaceFips = parts[29];
                     geocode.CensusStateFips = parts[30];

                     geocode.MatchedAddress = new RelaxableStreetAddress();
                     geocode.MatchedAddress.Number = parts[31];
                     geocode.MatchedAddress.NumberFractional = parts[32];
                     geocode.MatchedAddress.PreDirectional = parts[33];
                     geocode.MatchedAddress.PreQualifier = parts[34];
                     geocode.MatchedAddress.PreType = parts[35];
                     geocode.MatchedAddress.PreArticle = parts[36];
                     geocode.MatchedAddress.StreetName = parts[37];
                     geocode.MatchedAddress.PostArticle = parts[38];
                     geocode.MatchedAddress.PostQualifier = parts[39];
                     geocode.MatchedAddress.Suffix = parts[40];
                     geocode.MatchedAddress.PostDirectional = parts[41];
                     geocode.MatchedAddress.SuiteType = parts[42];
                     geocode.MatchedAddress.SuiteNumber = parts[43];
                     geocode.MatchedAddress.PostOfficeBoxType = parts[44];
                     geocode.MatchedAddress.PostOfficeBoxNumber = parts[45];
                     geocode.MatchedAddress.City = parts[46];
                     geocode.MatchedAddress.ConsolidatedCity = parts[47];
                     geocode.MatchedAddress.MinorCivilDivision = parts[48];
                     geocode.MatchedAddress.CountySubregion = parts[49];
                     geocode.MatchedAddress.County = parts[50];
                     geocode.MatchedAddress.State = parts[51];
                     geocode.MatchedAddress.ZIP = parts[52];
                     geocode.MatchedAddress.ZIPPlus1 = parts[53];
                     geocode.MatchedAddress.ZIPPlus2 = parts[54];
                     geocode.MatchedAddress.ZIPPlus3 = parts[55];
                     geocode.MatchedAddress.ZIPPlus4 = parts[56];
                     geocode.MatchedAddress.ZIPPlus5 = parts[57];

                     geocode.ParsedAddress = new StreetAddress();
                     geocode.ParsedAddress.Number = parts[58];
                     geocode.ParsedAddress.NumberFractional = parts[59];
                     geocode.ParsedAddress.PreDirectional = parts[60];
                     geocode.ParsedAddress.PreQualifier = parts[61];
                     geocode.ParsedAddress.PreType = parts[62];
                     geocode.ParsedAddress.PreArticle = parts[63];
                     geocode.ParsedAddress.StreetName = parts[64];
                     geocode.ParsedAddress.PostArticle = parts[65];
                     geocode.ParsedAddress.PostQualifier = parts[66];
                     geocode.ParsedAddress.Suffix = parts[67];
                     geocode.ParsedAddress.PostDirectional = parts[68];
                     geocode.ParsedAddress.SuiteType = parts[69];
                     geocode.ParsedAddress.SuiteNumber = parts[70];
                     geocode.ParsedAddress.PostOfficeBoxType = parts[71];
                     geocode.ParsedAddress.PostOfficeBoxNumber = parts[72];
                     geocode.ParsedAddress.City = parts[73];
                     geocode.ParsedAddress.ConsolidatedCity = parts[74];
                     geocode.ParsedAddress.MinorCivilDivision = parts[75];
                     geocode.ParsedAddress.CountySubregion = parts[76];
                     geocode.ParsedAddress.County = parts[77];
                     geocode.ParsedAddress.State = parts[78];
                     geocode.ParsedAddress.ZIP = parts[79];
                     geocode.ParsedAddress.ZIPPlus1 = parts[80];
                     geocode.ParsedAddress.ZIPPlus2 = parts[81];
                     geocode.ParsedAddress.ZIPPlus3 = parts[82];
                     geocode.ParsedAddress.ZIPPlus4 = parts[83];
                     geocode.ParsedAddress.ZIPPlus5 = parts[84];

                     geocode.MatchedFeatureAddress = new StreetAddress();
                     geocode.MatchedFeatureAddress.Number = parts[85];
                     geocode.MatchedFeatureAddress.NumberFractional = parts[86];
                     geocode.MatchedFeatureAddress.PreDirectional = parts[87];
                     geocode.MatchedFeatureAddress.PreQualifier = parts[88];
                     geocode.MatchedFeatureAddress.PreType = parts[89];
                     geocode.MatchedFeatureAddress.PreArticle = parts[90];
                     geocode.MatchedFeatureAddress.StreetName = parts[91];
                     geocode.MatchedFeatureAddress.PostArticle = parts[92];
                     geocode.MatchedFeatureAddress.PostQualifier = parts[93];
                     geocode.MatchedFeatureAddress.Suffix = parts[94];
                     geocode.MatchedFeatureAddress.PostDirectional = parts[95];
                     geocode.MatchedFeatureAddress.SuiteType = parts[96];
                     geocode.MatchedFeatureAddress.SuiteNumber = parts[97];
                     geocode.MatchedFeatureAddress.PostOfficeBoxType = parts[98];
                     geocode.MatchedFeatureAddress.PostOfficeBoxNumber = parts[99];
                     geocode.MatchedFeatureAddress.City = parts[100];
                     geocode.MatchedFeatureAddress.ConsolidatedCity = parts[101];
                     geocode.MatchedFeatureAddress.MinorCivilDivision = parts[102];
                     geocode.MatchedFeatureAddress.CountySubregion = parts[103];
                     geocode.MatchedFeatureAddress.County = parts[104];
                     geocode.MatchedFeatureAddress.State = parts[105];
                     geocode.MatchedFeatureAddress.ZIP = parts[106];
                     geocode.MatchedFeatureAddress.ZIPPlus1 = parts[107];
                     geocode.MatchedFeatureAddress.ZIPPlus2 = parts[108];
                     geocode.MatchedFeatureAddress.ZIPPlus3 = parts[109];
                     geocode.MatchedFeatureAddress.ZIPPlus4 = parts[110];
                     geocode.MatchedFeatureAddress.ZIPPlus5 = parts[111];

                     if (!String.IsNullOrEmpty(parts[112]))
                     {
                         geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Area = Convert.ToDouble(parts[112]);

                         if (StringUtils.IsDouble(parts[112]))
                         {
                             geocode.GeocodedError.ErrorBounds = Convert.ToDouble(parts[112]);
                         }
                     }

                     if (!String.IsNullOrEmpty(parts[113]))
                     {
                         Unit unit = UnitManager.FromString(parts[113]);
                         if (unit.UnitType == UnitTypes.Linear)
                         {
                             geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.AreaUnits = ((LinearUnit)unit).LinearUnitTypes;
                             geocode.GeocodedError.ErrorBoundsUnit = ((LinearUnit)unit).LinearUnitTypes;
                         }
                     }


                     string srid = parts[114];
                     if (!String.IsNullOrEmpty(srid))
                     {
                         geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID = Convert.ToInt32(srid);
                     }

                     string featureGml = parts[115];

                     if (!String.IsNullOrEmpty(featureGml))
                     {
                         try
                         {
                             StringReader stringReader = new StringReader(featureGml);
                             XmlReader xmlReader = XmlReader.Create(stringReader);
                             SqlXml sqlXml = new SqlXml(xmlReader);
                             geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry = SqlGeometry.GeomFromGml(sqlXml, geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID);

                             if (geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry != null && !geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry.IsNull)
                             {
                                 geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeography = SQLSpatialTools.SQLSpatialToolsFunctions.MakeValidGeographyFromGeometry(geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry);
                             }
                         }
                         catch (Exception ex)
                         {
                             geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Error = ex.Message;
                         }
                     }

                     geocode.SourceType = parts[116];
                     geocode.SourceVintage = parts[117];

                     geocode.MatchedFeature.PrimaryIdField = parts[118];
                     geocode.MatchedFeature.PrimaryIdValue = parts[119];
                     geocode.MatchedFeature.SecondaryIdField = parts[120];
                     geocode.MatchedFeature.SecondaryIdValue = parts[121];
                 }
             }
             else
             {
                 throw new Exception("Invalid return value from web service: " + webserviceResultString);
             }

             return geocode;
        }

        public static Geocode FromCsv_V2_95(StreetAddress streetAddress, string webserviceResultString, BaseOptions baseOptions)
        {
            Geocode geocode = new Geocode(baseOptions.Version);

            string[] parts = webserviceResultString.Split(',');
            if (parts.Length == 115)
            {
                if (parts != null)
                {

                    geocode.InputAddress = streetAddress;

                    geocode.TransactionId = parts[0];
                    geocode.Version = Convert.ToDouble(parts[1]);
                    geocode.SetQueryStatusCode(Convert.ToInt32(parts[2]));

                    if (geocode.QueryStatusCodes == QueryStatusCodes.Success)
                    {
                        geocode.Valid = true;
                    }

                    geocode.Geometry = new Point(Convert.ToDouble(parts[4]), Convert.ToDouble(parts[3]));
                    geocode.MatchScore = Convert.ToDouble(parts[5]);
                    //geocode.GeocodeQualityType = (GeocodeQualityType) Convert.ToInt32(parts[6]);
                    geocode.SetMatchType(parts[6]);
                    geocode.FM_GeographyType = (FeatureMatchingGeographyType)Enum.Parse(typeof(FeatureMatchingGeographyType), parts[7]);
                    geocode.InterpolationType = (InterpolationType)Enum.Parse(typeof(InterpolationType), parts[8]);
                    geocode.InterpolationSubType = (InterpolationSubType)Enum.Parse(typeof(InterpolationSubType), parts[9]);
                    geocode.SetMatchedLocationType(parts[10]);
                    geocode.FM_ResultType = (FeatureMatchingResultType)Enum.Parse(typeof(FeatureMatchingResultType), parts[11]);
                    geocode.TimeTaken = TimeSpan.FromSeconds(Convert.ToDouble(parts[12]));

                    geocode.CensusBlock = parts[13];
                    geocode.CensusBlockGroup = parts[14];
                    geocode.CensusTract = parts[15];
                    geocode.CensusCountyFips = parts[16];
                    geocode.CensusCbsaFips = parts[17];
                    geocode.CensusCbsaMicro = parts[18];
                    geocode.CensusMcdFips = parts[19];
                    geocode.CensusMetDivFips = parts[20];
                    geocode.CensusMsaFips = parts[21];
                    geocode.CensusPlaceFips = parts[22];
                    geocode.CensusStateFips = parts[23];

                    geocode.MatchedAddress = new RelaxableStreetAddress();
                    geocode.MatchedAddress.Number = parts[24];
                    geocode.MatchedAddress.NumberFractional = parts[25];
                    geocode.MatchedAddress.PreDirectional = parts[26];
                    geocode.MatchedAddress.PreQualifier = parts[27];
                    geocode.MatchedAddress.PreType = parts[28];
                    geocode.MatchedAddress.PreArticle = parts[29];
                    geocode.MatchedAddress.StreetName = parts[30];
                    geocode.MatchedAddress.PostArticle = parts[31];
                    geocode.MatchedAddress.PostQualifier = parts[32];
                    geocode.MatchedAddress.Suffix = parts[33];
                    geocode.MatchedAddress.PostDirectional = parts[34];
                    geocode.MatchedAddress.SuiteType = parts[35];
                    geocode.MatchedAddress.SuiteNumber = parts[36];
                    geocode.MatchedAddress.PostOfficeBoxType = parts[37];
                    geocode.MatchedAddress.PostOfficeBoxNumber = parts[38];
                    geocode.MatchedAddress.City = parts[39];
                    geocode.MatchedAddress.ConsolidatedCity = parts[40];
                    geocode.MatchedAddress.MinorCivilDivision = parts[41];
                    geocode.MatchedAddress.CountySubregion = parts[42];
                    geocode.MatchedAddress.County = parts[43];
                    geocode.MatchedAddress.State = parts[44];
                    geocode.MatchedAddress.ZIP = parts[45];
                    geocode.MatchedAddress.ZIPPlus1 = parts[46];
                    geocode.MatchedAddress.ZIPPlus2 = parts[47];
                    geocode.MatchedAddress.ZIPPlus3 = parts[48];
                    geocode.MatchedAddress.ZIPPlus4 = parts[49];
                    geocode.MatchedAddress.ZIPPlus5 = parts[50];

                    geocode.ParsedAddress = new StreetAddress();
                    geocode.ParsedAddress.Number = parts[51];
                    geocode.ParsedAddress.NumberFractional = parts[52];
                    geocode.ParsedAddress.PreDirectional = parts[53];
                    geocode.ParsedAddress.PreQualifier = parts[54];
                    geocode.ParsedAddress.PreType = parts[55];
                    geocode.ParsedAddress.PreArticle = parts[56];
                    geocode.ParsedAddress.StreetName = parts[57];
                    geocode.ParsedAddress.PostArticle = parts[58];
                    geocode.ParsedAddress.PostQualifier = parts[59];
                    geocode.ParsedAddress.Suffix = parts[60];
                    geocode.ParsedAddress.PostDirectional = parts[61];
                    geocode.ParsedAddress.SuiteType = parts[62];
                    geocode.ParsedAddress.SuiteNumber = parts[63];
                    geocode.ParsedAddress.PostOfficeBoxType = parts[64];
                    geocode.ParsedAddress.PostOfficeBoxNumber = parts[65];
                    geocode.ParsedAddress.City = parts[66];
                    geocode.ParsedAddress.ConsolidatedCity = parts[67];
                    geocode.ParsedAddress.MinorCivilDivision = parts[68];
                    geocode.ParsedAddress.CountySubregion = parts[69];
                    geocode.ParsedAddress.County = parts[70];
                    geocode.ParsedAddress.State = parts[71];
                    geocode.ParsedAddress.ZIP = parts[72];
                    geocode.ParsedAddress.ZIPPlus1 = parts[73];
                    geocode.ParsedAddress.ZIPPlus2 = parts[74];
                    geocode.ParsedAddress.ZIPPlus3 = parts[75];
                    geocode.ParsedAddress.ZIPPlus4 = parts[76];
                    geocode.ParsedAddress.ZIPPlus5 = parts[77];

                    geocode.MatchedFeatureAddress = new StreetAddress();
                    geocode.MatchedFeatureAddress.Number = parts[78];
                    geocode.MatchedFeatureAddress.NumberFractional = parts[79];
                    geocode.MatchedFeatureAddress.PreDirectional = parts[80];
                    geocode.MatchedFeatureAddress.PreQualifier = parts[81];
                    geocode.MatchedFeatureAddress.PreType = parts[82];
                    geocode.MatchedFeatureAddress.PreArticle = parts[83];
                    geocode.MatchedFeatureAddress.StreetName = parts[84];
                    geocode.MatchedFeatureAddress.PostArticle = parts[85];
                    geocode.MatchedFeatureAddress.PostQualifier = parts[86];
                    geocode.MatchedFeatureAddress.Suffix = parts[87];
                    geocode.MatchedFeatureAddress.PostDirectional = parts[88];
                    geocode.MatchedFeatureAddress.SuiteType = parts[89];
                    geocode.MatchedFeatureAddress.SuiteNumber = parts[90];
                    geocode.MatchedFeatureAddress.PostOfficeBoxType = parts[91];
                    geocode.MatchedFeatureAddress.PostOfficeBoxNumber = parts[92];
                    geocode.MatchedFeatureAddress.City = parts[93];
                    geocode.MatchedFeatureAddress.ConsolidatedCity = parts[94];
                    geocode.MatchedFeatureAddress.MinorCivilDivision = parts[95];
                    geocode.MatchedFeatureAddress.CountySubregion = parts[96];
                    geocode.MatchedFeatureAddress.County = parts[97];
                    geocode.MatchedFeatureAddress.State = parts[98];
                    geocode.MatchedFeatureAddress.ZIP = parts[99];
                    geocode.MatchedFeatureAddress.ZIPPlus1 = parts[100];
                    geocode.MatchedFeatureAddress.ZIPPlus2 = parts[101];
                    geocode.MatchedFeatureAddress.ZIPPlus3 = parts[102];
                    geocode.MatchedFeatureAddress.ZIPPlus4 = parts[103];
                    geocode.MatchedFeatureAddress.ZIPPlus5 = parts[104];

                    if (!String.IsNullOrEmpty(parts[105]))
                    {
                        geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Area = Convert.ToDouble(parts[105]);

                        if (StringUtils.IsDouble(parts[105]))
                        {
                            geocode.GeocodedError.ErrorBounds = Convert.ToDouble(parts[105]);
                        }
                    }

                    if (!String.IsNullOrEmpty(parts[106]))
                    {
                        Unit unit = UnitManager.FromString(parts[106]);
                        if (unit.UnitType == UnitTypes.Linear)
                        {
                            geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.AreaUnits = ((LinearUnit)unit).LinearUnitTypes;
                            geocode.GeocodedError.ErrorBoundsUnit = ((LinearUnit)unit).LinearUnitTypes;
                        }
                    }


                    string srid = parts[107];
                    if (!String.IsNullOrEmpty(srid))
                    {
                        geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID = Convert.ToInt32(srid);
                    }

                    string featureGml = parts[108];

                    if (!String.IsNullOrEmpty(featureGml))
                    {
                        try
                        {
                            StringReader stringReader = new StringReader(featureGml);
                            XmlReader xmlReader = XmlReader.Create(stringReader);
                            SqlXml sqlXml = new SqlXml(xmlReader);
                            geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry = SqlGeometry.GeomFromGml(sqlXml, geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SRID);

                            if (geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry != null && !geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry.IsNull)
                            {
                                geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeography = SQLSpatialTools.SQLSpatialToolsFunctions.MakeValidGeographyFromGeometry(geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.SqlGeometry);
                            }
                        }
                        catch (Exception ex)
                        {
                            geocode.MatchedFeature.MatchedReferenceFeature.StreetAddressableGeographicFeature.Geometry.Error = ex.Message;
                        }
                    }

                    geocode.SourceType = parts[109];
                    geocode.SourceVintage = parts[110];

                    geocode.MatchedFeature.PrimaryIdField = parts[111];
                    geocode.MatchedFeature.PrimaryIdValue = parts[112];
                    geocode.MatchedFeature.SecondaryIdField = parts[113];
                    geocode.MatchedFeature.SecondaryIdValue = parts[114];
                }
            }
            else
            {
                throw new Exception("Invalid return value from web service: " + webserviceResultString);
            }


            return geocode;
        }
    }
}
