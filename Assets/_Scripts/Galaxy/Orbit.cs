using UnityEngine;

namespace CLAY.Galaxy
{
    /// <summary>
    /// Classical Keplerian orbital elements → a 3D world position. Used to place stars/planets on
    /// inclined elliptical orbits in the viewer. Y-up; the reference plane is XZ, inclination tilts it.
    /// </summary>
    [System.Serializable]
    public struct OrbitElements
    {
        public float semiMajorAxisAU;   // a
        public float eccentricity;      // e (0 = circle)
        public float inclinationDeg;    // i (tilt of the orbital plane)
        public float ascendingNodeDeg;  // Ω (where it crosses the reference plane)
        public float argPeriapsisDeg;   // ω (orientation of the ellipse within its plane)
        public float meanAnomalyDeg;    // M at epoch (phase around the orbit)

        /// <summary>Orbital period in years (Kepler's third law, primary mass ~ solar).</summary>
        public float PeriodYears(float centralMassSolar) =>
            Mathf.Sqrt(Mathf.Max(1e-6f, semiMajorAxisAU * semiMajorAxisAU * semiMajorAxisAU / Mathf.Max(0.02f, centralMassSolar)));

        /// <summary>World position (AU) at the given mean anomaly (radians).</summary>
        public Vector3 PositionAt(float meanAnomalyRad)
        {
            float e = Mathf.Clamp(eccentricity, 0f, 0.9f);
            // Solve Kepler's equation  M = E − e·sinE  for eccentric anomaly E (Newton).
            float E = meanAnomalyRad;
            for (int k = 0; k < 6; k++)
                E -= (E - e * Mathf.Sin(E) - meanAnomalyRad) / (1f - e * Mathf.Cos(E));

            float nu = 2f * Mathf.Atan2(Mathf.Sqrt(1f + e) * Mathf.Sin(E * 0.5f),
                                        Mathf.Sqrt(1f - e) * Mathf.Cos(E * 0.5f));   // true anomaly
            float r = semiMajorAxisAU * (1f - e * Mathf.Cos(E));                     // radius

            // Position in the orbital (perifocal) plane, then rotate into the reference frame.
            Vector3 peri = new Vector3(r * Mathf.Cos(nu), 0f, r * Mathf.Sin(nu));
            return Rotation() * peri;
        }

        /// <summary>Orientation that lifts the perifocal plane into world space (node → inclination → arg).</summary>
        public Quaternion Rotation() =>
            Quaternion.AngleAxis(-ascendingNodeDeg, Vector3.up)
          * Quaternion.AngleAxis(-inclinationDeg, Vector3.right)
          * Quaternion.AngleAxis(-argPeriapsisDeg, Vector3.up);

        /// <summary>Position (AU) from eccentric anomaly directly — no Kepler solve, and evenly spread in
        /// space, so it's ideal for drawing a smooth orbit line at arbitrary resolution.</summary>
        public Vector3 PositionFromEccentric(float E)
        {
            float e = Mathf.Clamp(eccentricity, 0f, 0.9f);
            Vector3 peri = new Vector3(semiMajorAxisAU * (Mathf.Cos(E) - e), 0f,
                                       semiMajorAxisAU * Mathf.Sqrt(1f - e * e) * Mathf.Sin(E));
            return Rotation() * peri;
        }

        /// <summary>Sample <paramref name="pts"/>.Length points around the full ellipse (for the orbit line).</summary>
        public void SampleEllipse(Vector3[] pts)
        {
            int n = pts.Length;
            for (int i = 0; i < n; i++)
                pts[i] = PositionFromEccentric(i / (float)(n - 1) * Mathf.PI * 2f);
        }
    }
}
