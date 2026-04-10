# Syntax & Usage

The general format requires an input expression, the in operator, and one or more target units.
Basic Syntax

\[expression\] in \[target_unit\]

if no "in" is found, a suiting unit will be reduced to a suiting unit.
Sometimes SI base units, sometimes other tings.

## Mathematical Operators:

Supported operators follow standard order of operations: +, -, \*, /, ^, and (). Fractional exponents are supported
provided the resulting dimension vector contains only integers. km/h is TWO units, not one. Make sure to use parentheses accordingly

    10 m + 5 m in m = 15m

    (10 m)^2 in m2 = 10m2

    10 A * 5 ohm in V = 50V

But also advanced unit conversions, like calculating dilution:

    (2kg/yr) / (3m3/s) in ng/l = 21.1254 ng/l

Here we calculate the aerodynamic drag force on a car:

    Air Density: 1.225 kg/m^3
    Velocity: 120 km/h
    Drag Coefficient: 0.28
    Frontal Area: 2.2 m^2

    0.5 * 1.225 kg / m3 * (120 km / h)^2 * 0.28 * 2.2 m2 in N -> 419.2222 N

## Multi-Unit Output (Waterfall)

Targeting multiple units separated by commas triggers a waterfall calculation, yielding whole numbers for all
but the final unit.

    1.5 yr in d, h → 547 d 21 h

## Multi-Unit Input

Using a comma in the left-hand expression implicitly acts as addition for mixed-unit inputs.

    1 ft, 2 in in in → 14 in

# Supported Units

Units are resolved dynamically based on their underlying physical dimensions. Standard SI prefixes can be applied to
any base unit or mapped alias. Yes, the kilomile (kmi) is supported.

## Base Dimensions

+-------------+-------+-----------------------------------------------------------------+
| Dimension   | Unit  | Aliases                                                         |
+-------------+-------+-----------------------------------------------------------------+
| Length      | m     | meter(s), in, inch(es), ft, foot, feet, yd, yard(s), mi, mile,  |
|             |       |miles, nmi,                                                      |
|             |       | au, ly, pc, parsec,                                             |
+-------------+-------+-----------------------------------------------------------------+
| Mass        | kg    | g, gram, t, ton, tonne, lb, pound, oz, ounce, ton, u, amu, slug,|
|             |       |gr, grain, st, stone, short_ton, long_ton                        |
+-------------+-------+-----------------------------------------------------------------+
| Time        | s     | sec, second(s), min, minute(s), h, hr, hour(s), d, day(s), wk,  |
|             |       |week(s), yr, year(s)                                             |
+-------------+-------+-----------------------------------------------------------------+
| Current     | A     | Ampere                                                          |
+-------------+-------+-----------------------------------------------------------------+
| Photometry  | cd    | candela                                                         |
+-------------+-------+-----------------------------------------------------------------+
| Temperature | K     | kelvin, celsius, degC, fahrenheit, degF                         |
+-------------+-------+-----------------------------------------------------------------+

## Derived Dimensions

### Space & Mechanics

+-----------+-------------------------------------------------------------------------------------+
| Category  | Units                                                                               |
+-----------+-------------------------------------------------------------------------------------+
| Area      | m2, in2, ft2, are(s), acre(s), ha, hectare(s), barn                                 |
+-----------+-------------------------------------------------------------------------------------+
| Volume    |   m3, in3, ft3, l, liter(s), litre(s), gal, gallon(s), qt, quart(s), pt, pint(s),   |
|           |                                       fl_oz,                                        |
|           |                                   fluid_ounce(s),                                   |
|           |                                       fl_dr,                                        |
|           |                                         gi                                          |
+-----------+-------------------------------------------------------------------------------------+
| Force     | N,newton, lbf, kgf                                                                  |
+-----------+-------------------------------------------------------------------------------------+
| Pressure | Pa, bar, atm, atmosphere(s), psi, mHg, mH2O                                          |
+-----------+-------------------------------------------------------------------------------------+
| Energy    | J, joule(s),Nm, cal, BTU, eV, erg                                                   |
+-----------+-------------------------------------------------------------------------------------+
| Power     | W, watt(s), Wh, hp, horsepower                                                      |
+---------- +-------------------------------------------------------------------------------------+

### Cooking volume

+------------------+--------------------------------------------------+
| Category         | Units                                            |
+------------------+--------------------------------------------------+
| spoons           |  teaspoon(s), tablespoon(s), tsp, tbsp, us_tsp,  |
|                  |                     us_tbsp                      |
+------------------+--------------------------------------------------+
| cups             | cup(s),us_cup(s)                                 |
+------------------+--------------------------------------------------+

### Electromagnetism

+----------------+----------------+
| Category       | Units          |
+----------------+----------------+
| Charge         | C, coulomb     |
+----------------+----------------+
| Voltage        | V, volt        |
+----------------+----------------+
| Resistance     | ohm            |
+----------------+----------------+
| Capacitance    | F, fahrad      |
+----------------+----------------+
|     Inductance | H, henry, Oe,  |
|                | Mx             |
+----------------+----------------+
| Mag. Flux      | Wb, weber      |
+----------------+----------------+
| Mag. Field     | tesla, T_tesla |
+----------------+----------------+

### Angles and rotation

+-------------+---------------------+
| Category    | Units               |
+-------------+---------------------+
| radiant     | rad (dimensionless) |
+-------------+---------------------+
| degrees     | degrees, arcmin,    |
|             |arcsec               |
+-------------+---------------------+
| steradian   | sr                  |
+-------------+---------------------+
| revolutions | rev (2\*pi),rpm     |
+-------------+---------------------+

### Ratios

+---------------+---------------+
| Category      | Units         |
+---------------+---------------+
| percent       | %             |
+---------------+---------------+
| Parts per ... | ppm, ppb, ppt |
+---------------+---------------+

### Radioactivity

+----------+----------------------+
| Sievert  | sievert, Sv, rem     |
+----------+----------------------+
| Gray     |  gray, Gy, rad_dose  |
+----------+----------------------+
| Bequerel | bequerel, Bq, Ci     |
+----------+----------------------+

### Constants

+------------------+-------------+
| Category         | Units       |
+------------------+-------------+
| gravity          | gn          |
+------------------+-------------+
| pi               | pi          |
+------------------+-------------+
| speed of light   | c           |
+------------------+-------------+
| plancks constant | plank, hbar |
+------------------+-------------+

### Logarithmic units

So far only dB is supported. They do not behave like other units, but as functions/macros:

```
dBSPL(pressure) - reference micropascalse
dBW(power), dBM(power) -> reference W and milliW respectively.
dBV(voltage) -> reference voltage

```

# Engine Behavior

The engine is built on an Abstract Syntax Tree (AST) evaluated via a simple parser. It uses strict
dimensional analysis to validate all operations. It keeps strict dimensional integrity. Every unit
and constant maps to a base dimension vector (L,M,T,I,Temp, cd). Addition and subtraction require
strictly matching dimensions. Multiplication and division algebraically combine dimension vectors.

Standard SI prefixes (e.g., k, m, c, u) are automatically parsed and applied to base units. If a
unit string ends with a number (e.g., cm3), the engine recursively identifies the base unit,
scales the prefix multiplier by that exponent ((10−2)3), and applies the exponent to the base dimension (L3).

If the left and right expressions share the same dimension vector, in calculates the scalar ratio.
If the dimensions differ, in acts as a division operator. It calculates the algebraic quotientof the
scales and dimensions. If the resulting dimension vector matches a known alias (e.g., L2→m2), it
outputs that unit. Otherwise, it outputs the raw dimensional map (e.g., 10 (M^1 * T^-1)).

American Decimal Support: The parser actively normalizes dots surrounded by digits into commas prior
to evaluation (e.g., 2.5 becomes 2,5). 
