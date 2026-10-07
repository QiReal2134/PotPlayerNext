using System.Text;
using System.Xml;
using PotPlayerNext.Services;

public static class ImageFormatChecks
{
    public static int Run()
    {
        var count = 0;
        Check("SVG viewport and viewBox preserve dimensions", () =>
        {
            Require(Read("<svg width='800' height='400'/>") == (800u, 400u));
            Require(Read("<svg xmlns='http://www.w3.org/2000/svg' viewBox='-10, -20, 1200, 600'/>") == (1200u, 600u));
            Require(Read("<svg width='300' viewBox='0 0 4 3'/>") == (300u, 225u));
            Require(Read("<svg height='300' viewBox='0 0 4 3'/>") == (400u, 300u));
        });
        Check("SVG absolute units convert at 96 DPI", () =>
        {
            Require(Read("<svg width='1in' height='72pt'/>") == (96u, 96u));
            Require(Read("<svg width='6pc' height='96px'/>") == (96u, 96u));
            var metric = Read("<svg width='2.54cm' height='25.4mm'/>");
            Require(metric.Width is 96 or 97 && metric.Height is 96 or 97);
        });
        Check("SVG unknown and relative sizes use a finite viewport", () =>
        {
            Require(Read("<svg/>") == (300u, 150u));
            Require(Read("<svg width='100%' height='100%' viewBox='0 0 500 250'/>") == (500u, 250u));
            Require(Read("<svg width='NaN' height='Infinity' viewBox='0 0 -1 0'/>") == (300u, 150u));
            Require(Read("<svg width='1e309' height='-1'/>") == (300u, 150u));
        });
        Check("SVG rasterization is sharp and bounded on both axes", () =>
        {
            Require(SvgDimensions.RasterSize(300, 150, 2560) == (2560, 1280));
            Require(SvgDimensions.RasterSize(100, 100000, 192) == (1, 192));
            Require(SvgDimensions.RasterSize(uint.MaxValue, uint.MaxValue, 2560) == (2560, 2560));
            Require(SvgDimensions.RasterSize(0, 0, 192) == (192, 96));
            var huge = Read("<svg width='1e20' height='5e19'/>");
            Require(huge.Width == uint.MaxValue && huge.Height > 0);
            var raster = SvgDimensions.RasterSize(huge.Width, huge.Height, 192);
            Require(raster.Width == 192 && raster.Height is 95 or 96);
        });
        Check("SVG DTD and non-SVG documents are rejected without resolution", () =>
        {
            Expect<XmlException>(() => Read("<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///definitely-not-opened'>]><svg width='&x;'/>"));
            Expect<InvalidDataException>(() => Read("<html/>"));
            Expect<InvalidDataException>(() => Read("<svg xmlns='https://not-svg.invalid'/>"));
        });
        Check("SVG header parsing is bounded and leaves the stream owned by its caller", () =>
        {
            var overlong = "<!--" + new string('x', SvgDimensions.MaximumHeaderCharacters + 1) + "--><svg/>";
            Expect<XmlException>(() => Read(overlong));
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("<svg width='2' height='1'/><unread>"));
            Require(SvgDimensions.Read(input) == (2u, 1u) && input.CanRead);
        });
        Check("SVG raster budget rejects zero and int-overflow", () =>
        {
            Expect<ArgumentOutOfRangeException>(() => SvgDimensions.RasterSize(1, 1, 0));
            Expect<ArgumentOutOfRangeException>(() => SvgDimensions.RasterSize(1, 1, uint.MaxValue));
        });
        return count;

        void Check(string name, Action action) { action(); ++count; Console.WriteLine($"PASS: {name}"); }
    }

    private static (uint Width, uint Height) Read(string svg)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        return SvgDimensions.Read(stream);
    }
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Image format assertion failed.");
    }
    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
