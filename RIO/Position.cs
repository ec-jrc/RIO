//==========================================================================================
//
//		OpenNETCF.IO.Serial.GPS
//		Copyright (c) 2003-2006, OpenNETCF.org
//
//		This library is free software; you can redistribute it and/or modify it under 
//		the terms of the OpenNETCF.org Shared Source License.
//
//		This library is distributed in the hope that it will be useful, but 
//		WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or 
//		FITNESS FOR A PARTICULAR PURPOSE. See the OpenNETCF.org Shared Source License 
//		for more details.
//
//		You should have received a copy of the OpenNETCF.org Shared Source License 
//		along with this library; if not, email licensing@opennetcf.org to request a copy.
//
//		If you wish to contact the OpenNETCF Advisory Board to discuss licensing, please 
//		email licensing@opennetcf.org.
//
//		For general enquiries, email enquiries@opennetcf.org or visit our website at:
//		http://www.opennetcf.org
//
//==========================================================================================
using System;
using System.Text;
using System.Collections;
using System.Threading;
using System.IO;
using System.Globalization;

namespace RIO
{
    public enum CardinalDirection
    {
        /// <summary>
        /// North
        /// </summary>
        North = 0,
        /// <summary>
        /// East
        /// </summary>
        East = 1,
        /// <summary>
        /// South
        /// </summary>
        South = 2,
        /// <summary>
        /// West
        /// </summary>
        West = 4,
        /// <summary>
        /// Northwest
        /// </summary>
        NorthWest = 5,
        /// <summary>
        /// Northeast
        /// </summary>
        NorthEast = 6,
        /// <summary>
        /// Southwest
        /// </summary>
        SouthWest = 7,
        /// <summary>
        /// Southeast
        /// </summary>
        SouthEast = 8,
        /// <summary>
        /// Stationary
        /// </summary>
        Stationary = 9
    }

    public class Position
    {
        #region initialization
        // position stuff
        protected decimal latitude_fractional = 0;
        protected string latitude_sexagesimal = "";
        protected decimal latitude_decimal = 0;
        protected decimal latitude_decimal_mem = 0;
        protected CardinalDirection latitude_direction = CardinalDirection.North;

        protected decimal longitude_fractional = 0;
        protected string longitude_sexagesimal = "";
        protected decimal longitude_decimal = 0;
        protected decimal longitude_decimal_mem = 0;
        protected CardinalDirection longitude_direction = CardinalDirection.West;

        protected decimal altitudemax = 0;
        protected decimal altitude = 0;

        protected decimal geoidseparation = 0;

        protected DateTime sattime = DateTime.MinValue;
        protected DateTime satdate = DateTime.MinValue;

        protected uint satNumber = 0;
        #endregion

        #region properties

        public Position Clone()
        {
            Position newPos = new Position
            {
                latitude_fractional = latitude_fractional,
                latitude_sexagesimal = latitude_sexagesimal,
                latitude_decimal = latitude_decimal,
                latitude_decimal_mem = latitude_decimal_mem,
                latitude_direction = latitude_direction,

                longitude_fractional = longitude_fractional,
                longitude_sexagesimal = longitude_sexagesimal,
                longitude_decimal = longitude_decimal,
                longitude_decimal_mem = longitude_decimal_mem,
                longitude_direction = longitude_direction,

                altitudemax = altitudemax,
                altitude = altitude,

                geoidseparation = geoidseparation,

                sattime = sattime,
                satdate = satdate,

                satNumber = satNumber
            };

            return newPos;
        }

        public string Latitude_Sexagesimal
        {
            get
            {
                return latitude_sexagesimal;
            }
        }

        public string Longitude_Sexagesimal
        {
            get
            {
                return longitude_sexagesimal;
            }

        }

        public CardinalDirection DirectionLongitude
        {
            get
            {
                return longitude_direction;
            }
            set
            {
                longitude_direction = value;
                if ((longitude_direction == CardinalDirection.South && longitude_decimal > 0) ||
                  (longitude_direction == CardinalDirection.North && longitude_decimal < 0))
                    longitude_decimal = -longitude_decimal;
            }
        }

        public CardinalDirection DirectionLatitude
        {
            get
            {
                return latitude_direction;
            }
            set
            {
                latitude_direction = value;
                if ((latitude_direction == CardinalDirection.West && latitude_decimal > 0) ||
                  (latitude_direction == CardinalDirection.East && latitude_decimal < 0))
                    latitude_decimal = -latitude_decimal;
            }
        }

        public decimal Latitude_Fractional
        {
            get
            {
                return latitude_fractional;
            }
            set
            {
                latitude_fractional = value;
                latitude_sexagesimal = DecimalToSexagesimal(value);
                // if direction latitude is SOUTH * -1
                decimal Sens = 1;
                if (DirectionLatitude == CardinalDirection.South)
                    Sens = -1;
                latitude_decimal = FractionalToDecimalDegrees(value) * Sens;
            }

        }

        public decimal Latitude_Decimal
        {
            get
            {
                return latitude_decimal;
            }
        }

        public decimal Latitude_Decimal_Mem
        {
            set
            {
                latitude_decimal_mem = value;
            }
            get
            {
                return latitude_decimal_mem;
            }
        }
        public decimal Longitude_Fractional
        {
            get
            {
                return longitude_fractional;
            }
            set
            {
                longitude_fractional = value;
                longitude_sexagesimal = DecimalToSexagesimal(value);
                // if direction longitude is WEST * -1
                decimal Sens = 1;
                if (DirectionLatitude == CardinalDirection.West)
                    Sens = -1;
                longitude_decimal = FractionalToDecimalDegrees(value) * Sens;
            }
        }

        public decimal Longitude_Decimal
        {
            get
            {
                return longitude_decimal;
            }
        }
        public decimal Longitude_Decimal_Mem
        {
            set
            {
                longitude_decimal_mem = value;
            }
            get
            {
                return longitude_decimal_mem;
            }
        }

        public decimal Altitude
        {
            get
            {
                return altitude;
            }
            set
            {
                altitude = value;
                if (altitude > altitudemax)
                    altitudemax = altitude;
            }
        }
        public decimal AltitudeMax
        {
            get
            {
                return altitudemax;
            }
        }
        public decimal GeoidSeparation
        {
            get
            {
                return geoidseparation;
            }
            set
            {
                geoidseparation = value;
            }
        }
        public DateTime SatTime
        {
            get
            {
                return sattime;
            }
            set
            {
                sattime = value;
            }
        }

        public DateTime SatDate
        {
            get
            {
                return satdate;
            }
            set
            {
                satdate = value;
            }
        }

        public uint SatNumber { get => satNumber; set => satNumber = value; }

        #endregion

        /// <summary>
        /// Converts fractional degrees to decimal degrees
        /// </summary>
        /// <param name="decin"></param>
        /// <returns></returns>
        public static decimal FractionalToDecimalDegrees(decimal decin)
        {
            bool positve = decin > 0;
            string dm = Math.Abs(decin).ToString("00000.0000");

            //Get the fractional part of minutes
            int intdelim = dm.IndexOf(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

            decimal fm = ("0" + dm.Substring(intdelim)).ToDecimal();

            //Get the minutes.
            decimal min = dm.Substring(intdelim - 2, 2).ToDecimal();

            //Degrees
            decimal deg = dm.Substring(0, intdelim - 2).ToDecimal();

            decimal result = deg + (min + fm) / 60;
            if (positve)
                return result;
            else
                return -result;
        }

        //format (dd.dddd) to decimal minute format (ddmm.mmmm).
        //Example 58.65375° => 5839.225 (58° 39.225min)
        public static decimal DecimalToFractionalDegrees(decimal dec)
        {
            decimal d = (Decimal.Truncate(dec)); //58
            decimal m = (dec - d) * 60; //39.2250 
            m = Math.Round(m, 4); //39.2250
            return (d * 100) + m; //5839.2250
        }

        /// <summary>
        /// convert decimal degrees to sexagesimal format
        /// </summary>
        /// <param name="inputdata"></param>
        /// <returns></returns>
        /// 
        public static string DecimalToSexagesimal(decimal inputdata)
        {
            // orignal input = the data 4533.3512
            string strdata = Math.Abs(inputdata).ToString("00000.0000");

            // degrees is 45
            string degrees = strdata.Substring(0, 3);
            string minutes = strdata.Substring(3, 2);
            // minutes is 33
            decimal decseconds = strdata.Substring(5).ToDecimal();
            // decseconds = .3512
            decseconds = Math.Round(decseconds * 60, 2);

            // seconds = 21.07
            return degrees + "°" + minutes + "'" + decseconds.ToString("00.00") + "\"";
        }
    }
    public class LocationMetrics : Position, IMetrics
    {
        public LocationMetrics(double lon, double lat)
        {
            longitude_decimal = (decimal)lon;
            latitude_decimal = (decimal)lat;
        }
        public LocationMetrics(Position that)
        {
            latitude_fractional = that.Latitude_Fractional;
            latitude_sexagesimal = that.Latitude_Sexagesimal;
            latitude_decimal = that.Latitude_Decimal;
            latitude_decimal_mem = that.Latitude_Decimal_Mem;
            latitude_direction = that.DirectionLatitude;

            longitude_fractional = that.Longitude_Fractional;
            longitude_sexagesimal = that.Longitude_Sexagesimal;
            longitude_decimal = that.Longitude_Decimal;
            longitude_decimal_mem = that.Longitude_Decimal_Mem;
            longitude_direction = that.DirectionLongitude;

            altitudemax = that.AltitudeMax;
            altitude = that.Altitude;

            geoidseparation = that.GeoidSeparation;

            sattime = that.SatTime;
            satdate = that.SatDate;

            satNumber = that.SatNumber;
        }
    }
}
