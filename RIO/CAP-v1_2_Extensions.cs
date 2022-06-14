using JRC.CAP;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace RIO
{
    /// <summary>
    /// Accessories utulities to use an <see cref="alert"/>
    /// </summary>
    public static class CAP_v1_2_Extensions
    {
        /// <summary>
        /// Checks if address is in alert addresses and is used to select the alert regarding the device
        /// and discard the others.
        /// </summary>
        /// <param name="alert"><see cref="alert">alert</see> object</param>
        /// <param name="address">address to find</param>
        /// <returns></returns>
        public static bool HasInAddresses(this alert alert, string address)
        {
            if (string.IsNullOrWhiteSpace(alert.addresses)) return false;
            //$"((?<=\\s|\\b){address}(?=\\s|\\b))|((?<=\"){address}(?=\"))"
            Match match = Regex.Match(alert.addresses, $"((?<=\\s|\\b){address}(?=\\s|\\b))");
            return match.Value == address;
        }
        /// <summary>
        /// If <see cref="alertInfo"/> contains areas and the given coordinates are in one of them,
        /// returns true. It is used typically to check if the <see cref="alert"/> is related to the position of
        /// a device. Uses <see cref="Contains(alertInfoArea, double, double)"/>.
        /// </summary>
        /// <param name="alertInfo">One element of the <see cref="alert.info"/> collection of an <see cref="alert"/>.</param>
        /// <param name="latitude">The latitude of the position to check.</param>
        /// <param name="longitude">The longitude of the position to check.</param>
        /// <returns></returns>
        public static bool ContainsInArea(this alertInfo alertInfo, double latitude, double longitude)
        {
            if (alertInfo.area == null || alertInfo.area.Length == 0) return false;
            foreach (alertInfoArea area in alertInfo.area)
            {
                if (area.Contains(latitude, longitude)) return true;
            }

            return false;
        }
        /// <summary>
        /// If the <see cref="alertInfoArea"/> contains the given coordinates, returns true. It is used typically to check if the <see cref="alert"/> is related to the position of
        /// a device.
        /// </summary>
        /// <param name="area">One element of the <see cref="alertInfo.area"/> collection of an
        /// <see cref="alertInfo"/>, containing either a polygon or a circle..</param>
        /// <param name="latitude">The latitude of the position to check.</param>
        /// <param name="longitude">The longitude of the position to check.</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public static bool Contains(this alertInfoArea area, double latitude, double longitude)
        {
            if (area.circle != null && area.circle.Length > 0)
            {
                foreach (string circle in area.circle)
                {
                    string[] vs = circle.Split(new char[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    double circle_x = double.Parse(vs[0]),
                        circle_y = double.Parse(vs[1]),
                        rad = double.Parse(vs[2]);
                    if ((longitude - circle_x) * (longitude - circle_x) + (latitude - circle_y) * (latitude - circle_y) <= rad * rad)
                        return true;
                }
            }
            if (area.polygon != null && area.polygon.Length > 2)
            {
                return Contains(area.polygon.Select(s => s.Split(','))
                    .Select(sa => (double.Parse(sa[0]), double.Parse(sa[1]))).ToArray(),
                    latitude, longitude);
            }
            return false;
        }

        private static bool Contains((double, double)[] poly, double latitude, double longitude)
        {
            (double, double) p1, p2;
            bool inside = false;

            var oldPoint = poly[poly.Length - 1];

            for (int i = 0; i < poly.Length; i++)
            {
                var newPoint = poly[i];

                if (newPoint.Item2 > oldPoint.Item2)
                {
                    p1 = oldPoint;
                    p2 = newPoint;
                }
                else
                {
                    p1 = newPoint;
                    p2 = oldPoint;
                }

                if ((newPoint.Item2 < longitude) == (longitude <= oldPoint.Item2)
                    && (latitude - p1.Item1) * (p2.Item2 - p1.Item2)
                    < (p2.Item1 - p1.Item1) * (longitude - p1.Item2))
                {
                    inside = !inside;
                }

                oldPoint = newPoint;
            }

            return inside;
        }
    }
}
