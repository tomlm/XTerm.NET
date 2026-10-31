// Ported from libX11 src/xcms (LRGB.c, Lab.c, Luv.c, uvY.c, xyY.c, HVC.c), (c) 1990 1991 Tektronix, Inc.;
// its permission notice is reproduced in THIRD-PARTY-NOTICES.txt.
// The DEFAULT screen colorimetry -- the "Tektronix 19-inch (Sony) CRT" data every Xcms build
// ships -- because that is what xterm converts device-independent colour specs with, and what
// esctest's expected byte values were produced by. Byte-exactness is the point: a program that
// probes with CIELab and matches the reply needs OUR reply to be xterm's.

namespace XTerm.Common;

/// <summary>X Color Management System conversions for the device-independent colour spaces.</summary>
internal static class XcmsColor
{
    // XYZ -> linear RGB intensity, and its inverse; row sums of the inverse are the screen white.
    private static readonly double[,] XyzToRgb =
    {
        { 3.48340481253539000, -1.52176374927285200, -0.55923133354049780 },
        { -1.07152751306193600, 1.96593795204372400, 0.03673691339553462 },
        { 0.06351179790497788, -0.20020501000496480, 0.81070942031648220 },
    };

    private const double WhiteX = 0.38106149108714790 + 0.32025712365352110 + 0.24834578525933100;
    private const double WhiteY = 0.20729745115140850 + 0.68054638776373240 + 0.11215616108485920;
    private const double WhiteZ = 0.02133944350088028 + 0.14297193020246480 + 1.24172892629665500;

    private static readonly double[,] RgbToXyz =
    {
        { 0.38106149108714790, 0.32025712365352110, 0.24834578525933100 },
        { 0.20729745115140850, 0.68054638776373240, 0.11215616108485920 },
        { 0.02133944350088028, 0.14297193020246480, 1.24172892629665500 },
    };

    private static readonly (int Value, double Intensity)[] RedTbl =
    {
        (0x0000, 0.000000),
        (0x0909, 0.000000),
        (0x0a0a, 0.000936),
        (0x0f0f, 0.001481),
        (0x1414, 0.002329),
        (0x1919, 0.003529),
        (0x1e1e, 0.005127),
        (0x2323, 0.007169),
        (0x2828, 0.009699),
        (0x2d2d, 0.012759),
        (0x3232, 0.016392),
        (0x3737, 0.020637),
        (0x3c3c, 0.025533),
        (0x4141, 0.031119),
        (0x4646, 0.037431),
        (0x4b4b, 0.044504),
        (0x5050, 0.052373),
        (0x5555, 0.061069),
        (0x5a5a, 0.070624),
        (0x5f5f, 0.081070),
        (0x6464, 0.092433),
        (0x6969, 0.104744),
        (0x6e6e, 0.118026),
        (0x7373, 0.132307),
        (0x7878, 0.147610),
        (0x7d7d, 0.163958),
        (0x8282, 0.181371),
        (0x8787, 0.199871),
        (0x8c8c, 0.219475),
        (0x9191, 0.240202),
        (0x9696, 0.262069),
        (0x9b9b, 0.285089),
        (0xa0a0, 0.309278),
        (0xa5a5, 0.334647),
        (0xaaaa, 0.361208),
        (0xafaf, 0.388971),
        (0xb4b4, 0.417945),
        (0xb9b9, 0.448138),
        (0xbebe, 0.479555),
        (0xc3c3, 0.512202),
        (0xc8c8, 0.546082),
        (0xcdcd, 0.581199),
        (0xd2d2, 0.617552),
        (0xd7d7, 0.655144),
        (0xdcdc, 0.693971),
        (0xe1e1, 0.734031),
        (0xe6e6, 0.775322),
        (0xebeb, 0.817837),
        (0xf0f0, 0.861571),
        (0xf5f5, 0.906515),
        (0xfafa, 0.952662),
        (0xffff, 1.000000),
    };

    private static readonly (int Value, double Intensity)[] GreenTbl =
    {
        (0x0000, 0.000000),
        (0x1313, 0.000000),
        (0x1414, 0.000832),
        (0x1919, 0.001998),
        (0x1e1e, 0.003612),
        (0x2323, 0.005736),
        (0x2828, 0.008428),
        (0x2d2d, 0.011745),
        (0x3232, 0.015740),
        (0x3737, 0.020463),
        (0x3c3c, 0.025960),
        (0x4141, 0.032275),
        (0x4646, 0.039449),
        (0x4b4b, 0.047519),
        (0x5050, 0.056520),
        (0x5555, 0.066484),
        (0x5a5a, 0.077439),
        (0x5f5f, 0.089409),
        (0x6464, 0.102418),
        (0x6969, 0.116485),
        (0x6e6e, 0.131625),
        (0x7373, 0.147853),
        (0x7878, 0.165176),
        (0x7d7d, 0.183604),
        (0x8282, 0.203140),
        (0x8787, 0.223783),
        (0x8c8c, 0.245533),
        (0x9191, 0.268384),
        (0x9696, 0.292327),
        (0x9b9b, 0.317351),
        (0xa0a0, 0.343441),
        (0xa5a5, 0.370580),
        (0xaaaa, 0.398747),
        (0xafaf, 0.427919),
        (0xb4b4, 0.458068),
        (0xb9b9, 0.489165),
        (0xbebe, 0.521176),
        (0xc3c3, 0.554067),
        (0xc8c8, 0.587797),
        (0xcdcd, 0.622324),
        (0xd2d2, 0.657604),
        (0xd7d7, 0.693588),
        (0xdcdc, 0.730225),
        (0xe1e1, 0.767459),
        (0xe6e6, 0.805235),
        (0xebeb, 0.843491),
        (0xf0f0, 0.882164),
        (0xf5f5, 0.921187),
        (0xfafa, 0.960490),
        (0xffff, 1.000000),
    };

    private static readonly (int Value, double Intensity)[] BlueTbl =
    {
        (0x0000, 0.000000),
        (0x0e0e, 0.000000),
        (0x0f0f, 0.001341),
        (0x1414, 0.002080),
        (0x1919, 0.003188),
        (0x1e1e, 0.004729),
        (0x2323, 0.006766),
        (0x2828, 0.009357),
        (0x2d2d, 0.012559),
        (0x3232, 0.016424),
        (0x3737, 0.021004),
        (0x3c3c, 0.026344),
        (0x4141, 0.032489),
        (0x4646, 0.039481),
        (0x4b4b, 0.047357),
        (0x5050, 0.056154),
        (0x5555, 0.065903),
        (0x5a5a, 0.076634),
        (0x5f5f, 0.088373),
        (0x6464, 0.101145),
        (0x6969, 0.114968),
        (0x6e6e, 0.129862),
        (0x7373, 0.145841),
        (0x7878, 0.162915),
        (0x7d7d, 0.181095),
        (0x8282, 0.200386),
        (0x8787, 0.220791),
        (0x8c8c, 0.242309),
        (0x9191, 0.264937),
        (0x9696, 0.288670),
        (0x9b9b, 0.313499),
        (0xa0a0, 0.339410),
        (0xa5a5, 0.366390),
        (0xaaaa, 0.394421),
        (0xafaf, 0.423481),
        (0xb4b4, 0.453547),
        (0xb9b9, 0.484592),
        (0xbebe, 0.516587),
        (0xc3c3, 0.549498),
        (0xc8c8, 0.583291),
        (0xcdcd, 0.617925),
        (0xd2d2, 0.653361),
        (0xd7d7, 0.689553),
        (0xdcdc, 0.726454),
        (0xe1e1, 0.764013),
        (0xe6e6, 0.802178),
        (0xebeb, 0.840891),
        (0xf0f0, 0.880093),
        (0xf5f5, 0.919723),
        (0xfafa, 0.959715),
        (0xffff, 1.00000),
    };


    /// <summary>
    /// Parses one of the Xcms device-independent forms -- rgbi:, CIEXYZ:, CIExyY:, CIEuvY:,
    /// CIELab:, CIELuv:, TekHVC: -- to a 24-bit RGB, through exactly X11's pipeline: space to
    /// CIEXYZ against the screen white point, XYZ through the matrix to linear intensities,
    /// intensities through the per-channel tables to gun values.
    /// </summary>
    public static bool TryParse(string spec, out int rgb)
    {
        rgb = 0;
        var colon = spec.IndexOf(':');
        if (colon <= 0)
            return false;

        var prefix = spec[..colon];
        var parts = spec[(colon + 1)..].Split('/');
        if (parts.Length != 3)
            return false;

        var p = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out p[i]))
                return false;
        }

        double x, y, z;
        switch (prefix.ToLowerInvariant())
        {
            case "rgbi":
                return FromIntensities(p[0], p[1], p[2], out rgb);

            case "ciexyz":
                (x, y, z) = (p[0], p[1], p[2]);
                break;

            case "ciexyy":
                (x, y, z) = XyYToXyz(p[0], p[1], p[2]);
                break;

            case "cieuvy":
                (x, y, z) = UvYToXyz(p[0], p[1], p[2]);
                break;

            case "cielab":
                (x, y, z) = LabToXyz(p[0], p[1], p[2]);
                break;

            case "cieluv":
            {
                var (u, v, yy) = LuvToUvY(p[0], p[1], p[2]);
                (x, y, z) = UvYToXyz(u, v, yy);
                break;
            }

            case "tekhvc":
            {
                var (u, v, yy) = TekHvcToUvY(p[0], p[1], p[2]);
                (x, y, z) = UvYToXyz(u, v, yy);
                break;
            }

            default:
                return false;
        }

        var (r, g, b) = XyzToRgbi(x, y, z);

        // Out of gamut brings in Xcms's DEFAULT gamut compression, TekHVC ClipC: hold the hue
        // and value, walk the chroma back to the most saturated colour the screen has there.
        // This is why CIEXYZ:1/1/1 comes back as the screen's exact white and CIEuvY:.5/.5/.5
        // as a real colour rather than a clamp artifact -- and why the bytes match xterm's.
        const double eps = 0.001;
        if (Math.Min(r, Math.Min(g, b)) < -eps || Math.Max(r, Math.Max(g, b)) > 1.0 + eps)
        {
            (x, y, z) = ClipChroma(x, y, z);
            (r, g, b) = XyzToRgbi(x, y, z);
        }

        return FromIntensities(r, g, b, out rgb);
    }

    private static (double R, double G, double B) XyzToRgbi(double x, double y, double z)
        => (XyzToRgb[0, 0] * x + XyzToRgb[0, 1] * y + XyzToRgb[0, 2] * z,
            XyzToRgb[1, 0] * x + XyzToRgb[1, 1] * y + XyzToRgb[1, 2] * z,
            XyzToRgb[2, 0] * x + XyzToRgb[2, 1] * y + XyzToRgb[2, 2] * z);

    private static (double X, double Y, double Z) RgbiToXyz(double r, double g, double b)
        => (RgbToXyz[0, 0] * r + RgbToXyz[0, 1] * g + RgbToXyz[0, 2] * b,
            RgbToXyz[1, 0] * r + RgbToXyz[1, 1] * g + RgbToXyz[1, 2] * b,
            RgbToXyz[2, 0] * r + RgbToXyz[2, 1] * g + RgbToXyz[2, 2] * b);

    private static (double U, double V) WhiteUv()
    {
        var div = WhiteX + 15.0 * WhiteY + 3.0 * WhiteZ;
        return (4.0 * WhiteX / div, 9.0 * WhiteY / div);
    }

    private static (double U, double V, double Y) XyzToUvY(double x, double y, double z)
    {
        var div = x + 15.0 * y + 3.0 * z;
        if (div == 0.0)
        {
            var (wu, wv) = WhiteUv();
            return (wu, wv, y);
        }
        return (4.0 * x / div, 9.0 * y / div, y);
    }

    private static double ThetaOffsetDegrees()
    {
        const double uBestRed = 0.7127;
        const double vBestRed = 0.4931;
        var (wu, wv) = WhiteUv();
        return Math.Atan((vBestRed - wv) / (uBestRed - wu)) * 180.0 / Math.PI;
    }

    /// <summary>XcmsCIEuvYToTekHVC, with its quadrant walk transcribed exactly.</summary>
    private static (double H, double V, double C) UvYToTekHvc(double uPrime, double vPrime, double bigY)
    {
        const double chromaScale = 7.50725;
        const double eps = 0.001;
        var (wu, wv) = WhiteUv();
        var u = uPrime - wu;
        var v = vPrime - wv;

        double theta;
        if (u == 0.0)
        {
            theta = 0.0;
        }
        else
        {
            theta = Math.Atan(v / u) * 180.0 / Math.PI;
        }

        double lo = 0.0, hi = 360.0;
        if (u > 0.0 && v > 0.0) { lo = 0.0; hi = 90.0; }
        else if (u < 0.0 && v > 0.0) { lo = 90.0; hi = 180.0; }
        else if (u < 0.0 && v < 0.0) { lo = 180.0; hi = 270.0; }
        else if (u > 0.0 && v < 0.0) { lo = 270.0; hi = 360.0; }
        while (theta < lo) theta += 90.0;
        while (theta >= hi) theta -= 90.0;

        var l2 = bigY < 0.008856
            ? bigY * 903.29
            : Math.Cbrt(bigY) * 116.0 - 16.0;
        var c = l2 * chromaScale * Math.Sqrt(u * u + v * v);
        if (c < 0.0)
            theta = 0.0;

        var h = theta - ThetaOffsetDegrees();
        while (h < -eps) h += 360.0;
        while (h >= 360.0 + eps) h -= 360.0;
        return (h, l2, c);
    }

    /// <summary>_XcmsTekHVCQueryMaxVCRGB: the most saturated displayable colour on a hue.</summary>
    private static (double V, double C, double R, double G, double B) MaxVcRgb(double hue)
    {
        var (u, v, y) = TekHvcToUvY(hue, 40.0, 120.0);   // an unreachable colour on the hue
        var (x, yy, z) = UvYToXyz(u, v, y);
        var (r, g, b) = XyzToRgbi(x, yy, z);             // deliberately unclamped

        var small = Math.Min(r, Math.Min(g, b));
        r -= small; g -= small; b -= small;
        var large = Math.Max(r, Math.Max(g, b));
        r /= large; g /= large; b /= large;

        var (mx, my, mz) = RgbiToXyz(r, g, b);
        var (mu, mv, mY) = XyzToUvY(mx, my, mz);
        var (_, maxV, maxC) = UvYToTekHvc(mu, mv, mY);
        return (maxV, maxC, r, g, b);
    }

    /// <summary>XcmsTekHVCQueryMaxC: the maximum chroma at a given hue and value.</summary>
    private static (double H, double V, double C) MaxC(double hue, double value)
    {
        const double eps = 0.001;
        var (maxV, maxC, rs, gs, bs) = MaxVcRgb(hue);

        if (value <= maxV)
            return (hue, value, value * maxC / maxV);

        var nValue = value;
        var savedValue = value;
        var lastValue = -1.0;
        var lastChroma = -1.0;
        var maxDist = 100.0 - maxV;
        var rFactor = 1.0;
        var curV = 0.0;
        var curC = 0.0;

        for (var count = 0; count < 100; count++)
        {
            var prevValue = lastValue;
            lastValue = curV;
            lastChroma = curC;
            var nT = (nValue - maxV) / maxDist * rFactor;
            var r = rs * (1.0 - nT) + nT;
            var g = gs * (1.0 - nT) + nT;
            var b = bs * (1.0 - nT) + nT;
            var (x, y, z) = RgbiToXyz(r, g, b);
            var (u, v, yy) = XyzToUvY(x, y, z);
            (_, curV, curC) = UvYToTekHvc(u, v, yy);

            if (curV <= savedValue + eps && curV >= savedValue - eps)
                return (hue, curV, curC);

            nValue += savedValue - curV;
            if (nValue < maxV)
            {
                nValue = maxV;
                rFactor *= 0.5;
            }
            else if (nValue > 100.0)
            {
                if (Math.Abs(lastValue - savedValue) < Math.Abs(curV - savedValue))
                    return (hue, lastValue, lastChroma);
                return (hue, curV, curC);
            }
            _ = prevValue;
        }

        return (hue, curV, curC);
    }

    /// <summary>XcmsTekHVCClipC: hold hue and value, take the chroma the screen can show.</summary>
    private static (double X, double Y, double Z) ClipChroma(double x, double y, double z)
    {
        var (u, v, bigY) = XyzToUvY(x, y, z);
        var (h, hvcV, _) = UvYToTekHvc(u, v, bigY);
        var (rh, rv, rc) = MaxC(h, Math.Clamp(hvcV, 0.0, 100.0));
        var (cu, cv, cy) = TekHvcToUvY(rh, Math.Clamp(rv, 0.0, 100.0), Math.Max(rc, 0.0));
        return UvYToXyz(cu, cv, cy);
    }

    private static bool FromIntensities(double r, double g, double b, out int rgb)
    {
        // Out-of-gamut intensities are CLAMPED, which is Xcms's own no-compression fallback:
        // CIEXYZ:1/1/1 is brighter than the screen and comes back as the screen's white.
        var rv = ValueFromIntensity(RedTbl, Math.Clamp(r, 0.0, 1.0));
        var gv = ValueFromIntensity(GreenTbl, Math.Clamp(g, 0.0, 1.0));
        var bv = ValueFromIntensity(BlueTbl, Math.Clamp(b, 0.0, 1.0));
        rgb = ((rv >> 8) << 16) | ((gv >> 8) << 8) | (bv >> 8);
        return true;
    }

    /// <summary>
    /// _XcmsTableSearch + _XcmsIntensityInterpolation, bitsPerRGB = 8: binary-search the bracket,
    /// interpolate the 16-bit gun value, then snap to the nearest representable 8-bit-per-gun
    /// value exactly as the C does -- including its integer arithmetic.
    /// </summary>
    private static int ValueFromIntensity((int Value, double Intensity)[] tbl, double intensity)
    {
        const int bitsPerRgb = 8;
        if (intensity <= tbl[0].Intensity)
            return tbl[0].Value;
        if (intensity >= tbl[^1].Intensity)
            return tbl[^1].Value;

        var lo = 0;
        var hi = tbl.Length - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (tbl[mid].Intensity <= intensity)
                lo = mid;
            else
                hi = mid;
        }

        var ratio = (intensity - tbl[lo].Intensity) / (tbl[hi].Intensity - tbl[lo].Intensity);
        var target = (long)((tbl[hi].Value - tbl[lo].Value) * ratio) + tbl[lo].Value;

        const int shift = 16 - bitsPerRgb;
        const int maxColor = (1 << bitsPerRgb) - 1;
        var up = ((target >> shift) * 0xFFFF) / maxColor;
        long down;
        if (up < target)
        {
            down = up;
            up = (Math.Min((down >> shift) + 1, maxColor) * 0xFFFF) / maxColor;
        }
        else
        {
            down = (Math.Max((up >> shift) - 1, 0) * 0xFFFF) / maxColor;
        }

        var value = (up - target) < (target - down) ? up : down;
        return (int)value & 0xFFFF;
    }

    private static (double, double, double) XyYToXyz(double x, double y, double bigY)
    {
        const double eps = 0.00001;
        var div = (-2.0 * x) + (12.0 * y) + 3.0;
        if (div == 0.0)
            return (WhiteX, WhiteY, WhiteZ);
        var u = 4.0 * x / div;
        var v = 9.0 * y / div;
        div = (6.0 * u) - (16.0 * v) + 12.0;
        if (div == 0.0)
            div = eps;
        var sx = 9.0 * u / div;
        var sy = 4.0 * v / div;
        var sz = 1.0 - sx - sy;
        if (sy == 0.0)
            sy = eps;
        return (sx * bigY / sy, bigY, sz * bigY / sy);
    }

    private static (double, double, double) UvYToXyz(double u, double v, double bigY)
    {
        var div = (6.0 * u) - (16.0 * v) + 12.0;
        double sx, sy;
        if (div == 0.0)
        {
            var wu = 4.0 * WhiteX / (WhiteX + 15.0 * WhiteY + 3.0 * WhiteZ);
            var wv = 9.0 * WhiteY / (WhiteX + 15.0 * WhiteY + 3.0 * WhiteZ);
            div = (6.0 * wu) - (16.0 * wv) + 12.0;
            sx = 9.0 * wu / div;
            sy = 4.0 * wv / div;
        }
        else
        {
            sx = 9.0 * u / div;
            sy = 4.0 * v / div;
        }

        var sz = 1.0 - sx - sy;
        if (sy == 0.0)
            return (sx, bigY, sz);
        return (sx * bigY / sy, bigY, sz * bigY / sy);
    }

    private static (double, double, double) LabToXyz(double l, double a, double b)
    {
        var tmpL = (l + 16.0) / 116.0;
        var bigY = tmpL * tmpL * tmpL;
        if (bigY < 0.008856)
        {
            tmpL = l / 9.03292;
            return (WhiteX * ((a / 3893.5) + tmpL), tmpL, WhiteZ * (tmpL - (b / 1557.4)));
        }

        var fx = tmpL + (a / 5.0);
        var fz = tmpL - (b / 2.0);
        return (WhiteX * fx * fx * fx, bigY, WhiteZ * fz * fz * fz);
    }

    private static (double, double, double) LuvToUvY(double l, double u, double v)
    {
        var wDiv = WhiteX + 15.0 * WhiteY + 3.0 * WhiteZ;
        var wu = 4.0 * WhiteX / wDiv;
        var wv = 9.0 * WhiteY / wDiv;

        double bigY;
        if (l < 7.99953624)
        {
            bigY = l / 903.29;
        }
        else
        {
            var t = (l + 16.0) / 116.0;
            bigY = t * t * t;
        }

        if (l == 0.0)
            return (wu, wv, bigY);

        var tmp = 13.0 * (l / 100.0);
        return (u / tmp + wu, v / tmp + wv, bigY);
    }

    private static (double, double, double) TekHvcToUvY(double h, double bigV, double c)
    {
        const double uBestRed = 0.7127;
        const double vBestRed = 0.4931;
        const double chromaScale = 7.50725;

        var wDiv = WhiteX + 15.0 * WhiteY + 3.0 * WhiteZ;
        var wu = 4.0 * WhiteX / wDiv;
        var wv = 9.0 * WhiteY / wDiv;

        if (bigV == 0.0 || bigV == 100.0)
            return (wu, wv, bigV == 100.0 ? 1.0 : 0.0);

        var thetaOffset = Math.Atan((vBestRed - wv) / (uBestRed - wu)) * 180.0 / Math.PI;
        var hue = h + thetaOffset;
        while (hue < 0.0) hue += 360.0;
        while (hue >= 360.0) hue -= 360.0;
        hue = hue * Math.PI / 180.0;

        var u = Math.Cos(hue) * c / (bigV * chromaScale);
        var v = Math.Sin(hue) * c / (bigV * chromaScale);

        double bigY;
        if (bigV < 7.99953624)
        {
            bigY = bigV / 903.29;
        }
        else
        {
            var t = (bigV + 16.0) / 116.0;
            bigY = t * t * t;
        }

        return (u + wu, v + wv, bigY);
    }
}
