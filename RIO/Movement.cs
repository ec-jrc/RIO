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
    /// <summary>
    /// The class is used to express a displacement, starting from GPS based information.
    /// </summary>
    public class Movement
    {
        #region Initialization
        int nbspeedvalues = 0;
        decimal speedknotssum = 0;

        decimal speedknotsaverage = 0;
        decimal speedknots = 0;
        decimal speedknotsmax = 0;

        decimal speedmphaverage = 0;
        decimal speedmph = 0;
        decimal speedmphmax = 0;

        decimal speedkphaverage = 0;
        decimal speedkph = 0;
        decimal speedkphmax = 0;

        decimal track = 0;

        //decimal magneticvariation=0;
        decimal magneticvariation;

        //CardinalDirection directionmagnetic=CardinalDirection.West;
        CardinalDirection directionmagnetic;

        #endregion

        #region properties
        /// <summary>
        /// Number of values of speed computed.
        /// </summary>
        public int NbSpeedValues
        {
            set
            {
                nbspeedvalues = value;
            }
            get
            {
                return nbspeedvalues;
            }
        }
        /// <summary>
        /// The variation of direction of the displacement related to the magentic north pole
        /// </summary>
        public decimal MagneticVariation
        {
            get
            {
                return magneticvariation;
            }
            set
            {
                magneticvariation = value;
            }
        }
        /// <summary>
        /// The current direction of the displacement related to the magentic north pole
        /// </summary>
        public CardinalDirection DirectionMagnetic
        {
            get
            {
                return directionmagnetic;
            }
            set
            {
                directionmagnetic = value;
            }
        }
        /// <summary>
        /// The average speed expressed in nautical miles per hour.
        /// </summary>
        public decimal SpeedKnotsAverage
        {
            get
            {
                return speedknotsaverage;
            }
        }
        /// <summary>
        /// The maximum speed expressed in nautical miles per hour.
        /// </summary>
        public decimal SpeedKnotsMax
        {
            get
            {
                return speedknotsmax;
            }
        }
        /// <summary>
        /// The speed expressed in nautical miles per hour. All ceonverted values are updated.
        /// </summary>
        public decimal SpeedKnots
        {
            get
            {
                return speedknots;
            }
            set
            {
                speedknots = Math.Round(value, 3);
                speedknotssum = speedknotssum + speedknots;
                if (speedknots > speedknotsmax)
                {
                    speedknotsmax = Math.Round(speedknots, 3);
                    speedmphmax = Math.Round(KnotsToMph(speedknots), 3);
                    speedkphmax = Math.Round(KnotsToKph(speedknots), 3);
                }
                speedmph = Math.Round(KnotsToMph(speedknots), 3);
                speedkph = Math.Round(KnotsToKph(speedknots), 3);

                if (nbspeedvalues > 0)
                {
                    speedknotsaverage = Math.Round(speedknotssum / nbspeedvalues, 3);
                    speedmphaverage = Math.Round(KnotsToMph(speedknotsaverage), 3);
                    speedkphaverage = Math.Round(KnotsToKph(speedknotsaverage), 3);
                }
            }
        }
        /// <summary>
        /// The average speed during the displacement expressed in m/h.
        /// </summary>
        public decimal SpeedMphAverage
        {
            get
            {
                return speedmphaverage;
            }
        }
        /// <summary>
        /// The maximum speed during the displacement expressed in m/h.
        /// </summary>
        public decimal SpeedMphMax
        {
            get
            {
                return speedmphmax;
            }
        }
        /// <summary>
        /// The speed of the displacement expressed in m/h.
        /// </summary>
        public decimal SpeedMph
        {
            get
            {
                return speedmph;
            }

        }
        /// <summary>
        /// The average speed during the displacement expressed in km/h.
        /// </summary>
        public decimal SpeedKphAverage
        {
            get
            {
                return speedkphaverage;
            }
        }
        /// <summary>
        /// The maximum speed during the displacement expressed in km/h.
        /// </summary>
        public decimal SpeedKphMax
        {
            get
            {
                return speedkphmax;
            }
        }
        /// <summary>
        /// The speed of the displacement expressed in km/h.
        /// </summary>
        public decimal SpeedKph
        {
            get
            {
                return speedkph;
            }
        }
        /// <summary>
        /// Geograhic heading expressed in decimal degrees.
        /// </summary>
        public decimal Track
        {
            get
            {
                return Math.Round(track, 1);
            }
            set
            {
                track = value;
            }
        }
        #endregion
        /// <summary>
        /// Converts a knots expressed speed in m/h.
        /// </summary>
        /// <param name="Knots">The speed expressed in nautical miles per hour.</param>
        /// <returns>The speed expressed in terrestrial miles per hour.</returns>
        public static decimal KnotsToMph(decimal Knots)
        {
            return Knots * 1.151m;
        }
        /// <summary>
        /// Converts a knots expressed speed in km/h.
        /// </summary>
        /// <param name="Knots">The speed expressed in nautical miles per hour.</param>
        /// <returns>The speed expressed in kilometres per hour.</returns>
        public static decimal KnotsToKph(decimal Knots)
        {
            return Knots * 1.852m;
        }
    }
}
