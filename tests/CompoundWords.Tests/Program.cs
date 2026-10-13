using WordLens;

// A timeout fails the regression without blocking the test runner indefinitely.
await Task.Run(() =>
{
    foreach (int length in new[] { 40, 80, 119 })
    {
        Expect(new string('a', length) + "1");
        Expect(new string('a', length) + "-");
    }
}).WaitAsync(TimeSpan.FromSeconds(2));

Expect("response-conventions", "response", "conventions");
Expect("response_conventions", "response", "conventions");
Expect("responseConventions", "response", "Conventions");
Expect("HTTPServer", "HTTP", "Server");
Expect("mother-in-law", "mother", "in", "law");
Expect("(response-conventions)", "response", "conventions");
foreach (char separator in new[] { '\u2010', '\u2011', '\u2013', '\u2014' })
    Expect($"response{separator}conventions", "response", "conventions");
Expect("don't");
Expect("don’t");
Expect("unknownword");
Expect("This is a sentence.");
Expect("response--conventions");
Expect("response__conventions");
Expect("");
Expect(new string('a', 118) + "-b", new string('a', 118), "b");
Expect(new string('a', 119) + "-b");
Expect(string.Join('-', Enumerable.Repeat("a", 12)), Enumerable.Repeat("a", 12).ToArray());
Expect(string.Join('-', Enumerable.Repeat("a", 13)));
Console.WriteLine("27 compound-word regression cases passed.");

static void Expect(string input, params string[] expected)
{
    string[] actual = CompoundWords.Split(input);
    if (!actual.SequenceEqual(expected))
        throw new InvalidOperationException($"Unexpected split for {input}: {string.Join('|', actual)}");
}
