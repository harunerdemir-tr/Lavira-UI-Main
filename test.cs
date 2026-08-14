using System;
using System.Globalization;
class Program {
    static void Main() {
        bool b = double.TryParse("+2.1", NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
        Console.WriteLine(b + " " + v);
    }
}
