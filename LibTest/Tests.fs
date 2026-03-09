module Tests

open System
open Umits
open Xunit



type Fixture() =
    do
        Umits.ConfigurationLoader.loadAll()
        ()
    


type EngineTest(fixture: Fixture) =
    let run query = 
        Engine.convertQuery query
    
    
    [<Fact>]
    let ``Basic arithmetic evaluates correctly`` () =
        Assert.Equal("25", run "5 * 5")
        Assert.Equal("15", run "10 + 5")
        Assert.Equal("100", run "10^2")

    [<Fact>]
    let ``Implicit multiplication evaluates correctly`` () =
        Assert.Equal("500", run "5(100)")
        Assert.Equal("25", run "5(5)")

    [<Fact>]
    let ``Unit arithmetic and conversions evaluate correctly`` () =
        Assert.Equal("10 m", run "5m + 5m")
        Assert.Equal("1000 m", run "1km in m")
        Assert.Equal("1000", run "1kg / 1g")

    [<Fact>]
    let ``Implicit unit composition evaluates correctly`` () =
        // Adjust the expected string to match how your engine formats unit output
        Assert.Equal("5 (L^1 * M^1)", run "5 kg m") 
        Assert.Equal("10 J", run "10 N m") // If your engine auto-resolves to J

    [<Fact>]
    let ``Functions evaluate correctly`` () =
        Assert.Equal("2", run "log10(100)")
        Assert.Equal("3", run "log(2, 8)")

    [<Fact>]
    let ``Macros expand and evaluate correctly`` () =
        // Adjust expected values based on your engine's decimal rounding
        Assert.Equal("16.9897 dB", run "dBW(50W)")
        
    [<Fact>]
    let ``Complex dimensional analysis and implicit multiplication`` () =
        // Nested parentheses with derived unit conversion
        Assert.Equal("25 N", run "(10 kg * (5 m / 2 s^2)) in N")
        
        // Implicit multiplication with mixed dimension strings
        Assert.Equal("30 (L^2 * M^1)", run "5(2 kg)(3 m^2)")
        
        // Prefix cancellation across division
        Assert.Equal("10000 m/s", run "10 km / 1 s in m/s")
        Assert.Equal("1", run "1 kg / 1000 g")

    [<Fact>]
    let ``Entity resolution with SI prefixes and unit conversions`` () =
        // The specific cholesterol query from earlier
        // (Assumes you have cholesterol mapped with molar_mass = 386.65 g/mol)
        // TODO: load the predefined entities
        // Assert.Equal("193.3 mg/dl", run "5 * milli(cholesterol.molar_mass)/l in mg/dl")
        ()

    [<Fact>]
    let ``Nested functions, macros, and logarithmic conversions`` () =
        // Macro with internal arithmetic
        Assert.Equal("16.9897 dB", run "dBW(25W + 25W)")

    [<Fact>]
    let ``Expected error handling for invalid operations`` () =
        // The specific safeguard we built for square roots of odd powers
        Assert.Contains("odd power", run "sqrt(5 m^3)")
        
        // Adding incompatible units
        Assert.Contains("Dimension", run "5m + 5kg")
        
        // Logarithm of a dimensioned quantity (since log requires a dimensionless ratio)
        Assert.Contains("dimensionless", run "log10(50 W)")
    interface IClassFixture<Fixture>
