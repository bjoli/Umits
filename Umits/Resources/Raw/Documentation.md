# Syntax & Usage

The general format requires an input expression, the in operator, and one or more target units.
Basic Syntax

\[expression\] in \[target_unit\]

if no "in" is found, a suiting unit will be reduced to a suiting unit. Sometimes SI base units, sometimes other tings.

## Mathematical Operators:
Supported operators follow standard order of operations: +, -, *, /, ^, and (). Fractional exponents are supported
provided the resulting dimension vector contains only integers.

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
+--------------------+--------------+------------------------------------------------------+
| Dimension          | Primary Base | Supported Aliases                                    |
+--------------------+--------------+------------------------------------------------------+
| Length (L)         | m            | in, ft, yd, mi, nmi, au, ly, pc                      |
+--------------------+--------------+------------------------------------------------------+
| Mass (M)           | kg           | g, lb, oz, ton, u, amu                               |
+--------------------+--------------+------------------------------------------------------+
| Time (T)           | s            | min, h, hr, d, wk, yr day, week, yr, year            |
+--------------------+--------------+------------------------------------------------------+
| Current (I)        | A            |                                                      |
+--------------------+--------------+------------------------------------------------------+
| Photometry (Cd)    | cd           |                                                      |
+--------------------+--------------+------------------------------------------------------+
| Temperature (Temp) | K            | degC, degF                                           |
+--------------------+--------------+------------------------------------------------------+

## Derived Dimensions

### Space & Mechanics
+---------------------+-------------------------------------+
| Category            | Units                               |
+---------------------+-------------------------------------+
| Area (L2)           | m2, in2, ft2, are, acre, ha, barn   |
+---------------------+-------------------------------------+
| Volume (L3)         | m3, in3, ft3, l, gal, qt, pt, fl_oz |
+---------------------+-------------------------------------+
| Force (M⋅L/T2)      | N, lbf                              |
+---------------------+-------------------------------------+
| Pressure (M/(L⋅T2)) | Pa, bar, atm, psi                   |
+---------------------+-------------------------------------+
| Energy (M⋅L2/T2)    | J, Nm, cal, BTU, eV                 |
+---------------------+-------------------------------------+
| Power (M⋅L2/T3)     | W, Wh, hp                           |
+---------------------+-------------------------------------+

### Electromagnetism
+----------------------------+----------------+
| Category                   | Units          |
+----------------------------+----------------+
| Charge (I⋅T)               | C              |
+----------------------------+----------------+
| Voltage (M⋅L2/(I⋅T3))      | V              |
+----------------------------+----------------+
| Resistance (M⋅L2/(I2⋅T3))  | ohm            |
+----------------------------+----------------+
| Capacitance (I2⋅T4/(M⋅L2)) | F              |
+----------------------------+----------------+
| Inductance (M⋅L2/(I2⋅T2))  | H              |
+----------------------------+----------------+
| Mag. Flux (M⋅L2/(I⋅T2))    | W              |
+----------------------------+----------------+
| Mag. Field (M/(I⋅T2))      | tesla, T_tesla |
+----------------------------+----------------+

### Angles and rotation
+-----------------------------------+
| radiant     | rad (dimensionless) |
| degrees     | degrees             |
| steradian   | sr                  |
| revolutions | rev (2*pi),rpm      |
+-----------------------------------+

### Ratios
+---------------+---------------+
| percent       | %             |
| Parts per ... | ppm, ppb, ppt |
+---------------+---------------+

### Constants
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
dBSPL(pressure) returns micropascals


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
