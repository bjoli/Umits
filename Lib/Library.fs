(*
Umits Engine - an algebraic engine
Copyright (C) 2026 Linus Björnstam

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.

---

App Store Distribution Exception
(Additional Permission under Section 7 of the GNU General Public License, version 3)

As a special exception to the GPLv3, the copyright holders grant you permission to
compile and publish this software to a digital marketplace (such as the Apple App
Store) whose Terms of Service or Digital Rights Management (DRM) requirements would
otherwise conflict with the conditions of the GPLv3.

This exception applies strictly under the following conditions:

* Permitted Modifications: You may only make the technical modifications strictly
  necessary to comply with the digital marketplace’s submission requirements (e.g.,
  modifying bundle IDs, API keys, or signing certificates).
* Restrictions: You may not modify the software's core functionality or create
  derivative works for other purposes under this exception. Any such modifications
  immediately void this exception, and the resulting work becomes subject entirely
  to the standard terms of the GPLv3, including the requirement to release the
  complete corresponding source code.
* No Sublicensing: You may not sublicense the specific rights granted by this
  exception to any third party.

If you modify this program, you may extend this exception to your version of the
program, but you are not obligated to do so. If you do not wish to do so, delete
this exception statement from your version.
*)


namespace Umits

open System
open System.Text.RegularExpressions
open System.Collections.Generic
open FParsec

(*
So. This is where I excuse myself. When I started this project, I knew no F sharp. I still don't.
I just sort of wrote some code that I thought would work, and then threw garbage at the compiler
until it stopped complaining. While I do know how and why it works, I have no real idea why it
actually compiles.

This is a rather simple algebraic engine. It defines 7 dimensions, and defines all other units
in terms of those dimensions. It can be simple units, like a foot that is just a linear conversion,
or compound units like a Henry(V*S/A). 

First we parse in all the units, and define all aliases in terms of the base dimensions.

Say we get an expresion like this (5m + 5ft)/s. First we normalize 5ft to the base unit and add
them together. Then we get scale 6.524 in the L1 dimension (linear length).Then we evaluate the right
hand side to 1.0s

Then we divide the scales (6.524/1.0 is 6.524) and subtract the right dimension from the left, meaning
we get a final record of scale=6.254 and dims (dimensions) = ["L", 1; "T", -1] (before subtraction, the
T dimension was implicitly 0). We can then use this to find the best representation, which the code
below makes to m/s.

If you want to read about it, read about dimensional analysis. The example above is just an example
of the standard algebraic rule of exponents.
*)

module Engine =

    // This can be used to set the format string.
    // of course not thread safe.
    let mutable formatString = "G7"
    // --- 1. Core Dimensions ---
    type Dims = Map<string, int>

    let L = Map [ "L", 1 ] // Length
    let M = Map [ "M", 1 ] //mass
    let T = Map [ "T", 1 ] //time
    let Temp = Map [ "Temp", 1 ] //temp
    let Info = Map [ "Info", 1 ] //IT
    let I = Map [ "I", 1 ] // current
    let Cd = Map [ "Cd", 1 ] // photometry
    let Mol = Map [ "Mol", 1 ]

    let addDims (d1: Dims) (d2: Dims) : Dims =
        Map.fold (fun acc k v2 ->
            let v1 = Map.tryFind k acc |> Option.defaultValue 0
            let sum = v1 + v2
            if sum = 0 then Map.remove k acc else Map.add k sum acc
        ) d1 d2

    let subDims (d1: Dims) (d2: Dims) : Dims =
        Map.fold (fun acc k v2 ->
            let v1 = Map.tryFind k acc |> Option.defaultValue 0
            let diff = v1 - v2
            if diff = 0 then Map.remove k acc else Map.add k diff acc
        ) d1 d2

    // --- 2. UnitDef & Database ---
    type UnitDef =
        { Scale: float
          Offset: float
          Dims: Dims }

    let linear scale dims =
        { Scale = scale
          Offset = 0.0
          Dims = dims }

    let prefixes =
        [ ("Q", 1e30)
          ("R", 1e27)
          ("Y", 1e24)
          ("Z", 1e21)
          ("E", 1e18)
          ("P", 1e15)
          ("T", 1e12)
          ("G", 1e9)
          ("M", 1e6)
          ("k", 1e3)
          ("h", 1e2)
          ("da", 1e1)
          ("d", 1e-1)
          ("c", 1e-2)
          ("m", 1e-3)
          ("u", 1e-6)
          ("n", 1e-9)
          ("p", 1e-12)
          ("f", 1e-15)
          ("a", 1e-18)
          ("z", 1e-21)
          ("y", 1e-24)
          ("r", 1e-27)
          ("q", 1e-30)
          ("Yi", 1208925819614629174706176.0)
          ("Zi", 1180591620717411303424.0)
          ("Ei", 1152921504606846976.0)
          ("Pi", 1125899906842624.0)
          ("Ti", 1099511627776.0)
          ("Gi", 1073741824.0)
          ("Mi", 1048576.0)
          ("Ki", 1024.0)
          ("ki", 1024.0) ]
        |> List.sortByDescending (fun (p, _) -> p.Length)

    let unitDb = Dictionary<string, UnitDef>()

    let private powerRegex = Regex(@"^([a-zA-Z_]+)(\d+)$", RegexOptions.Compiled)
    let rec resolveSingleUnit (u: string) =
        if unitDb.ContainsKey(u) then
            Some unitDb[u]
        else
            // Check if the string ends with a number (e.g., "in2", "cm3")
            let powerMatch = powerRegex.Match(u)

            if powerMatch.Success then
                let baseStr = powerMatch.Groups[1].Value
                let power = float powerMatch.Groups[2].Value

                // Recursively resolve the base unit and apply the exponent
                match resolveSingleUnit baseStr with
                | Some b ->
                    let newDims =
                        b.Dims |> Map.map (fun _ v -> v * int power) |> Map.filter (fun _ v -> v <> 0)

                    Some
                        { Scale = b.Scale ** power
                          Offset = 0.0
                          Dims = newDims }
                | None -> None
            else
                // Fallback to checking for SI prefixes
                match prefixes |> List.tryFind (fun (p, _) -> u.StartsWith(p)) with
                | Some(p, mult) ->
                    let baseUnit = u.Substring(p.Length)

                    match resolveSingleUnit baseUnit with
                    | Some b ->
                        Some
                            { Scale = b.Scale * mult
                              Offset = b.Offset
                              Dims = b.Dims }
                    | None -> None
                | None -> None

    // --- 3. AST and Parser ---
    type Expr =
        | Num of float
        | UnitCoeff of float * UnitDef
        | Group of Expr
        | Add of Expr * Expr
        | Sub of Expr * Expr
        | Mul of Expr * Expr
        | Div of Expr * Expr
        | Pow of Expr * Expr
        | Func of string * Expr list
        
    // Thanks to alexei for pointing out I should use a builder.
    type ResultBuilder() =
        member _.Bind(m, f) = Result.bind f m
        member _.Return(x) = Result.Ok x
        member _.ReturnFrom(x) = x
    
    let result = ResultBuilder()
    let rec eval expr =
        match expr with
        | Num n -> Result.Ok(linear n Map.empty)
        | UnitCoeff(coeff, def) ->
            Result.Ok
                { Scale = coeff * def.Scale
                  Offset = def.Offset
                  Dims = def.Dims }
        | Group e -> eval e
        | Add(x, y) ->
            result {
                let! vx = eval x
                let! vy = eval y
                if vx.Dims = vy.Dims then
                    return { Scale = vx.Scale + vy.Scale; Offset = vx.Offset; Dims = vx.Dims }
                else
                    return! Result.Error "Dimensional mismatch in addition"
            }
        | Sub(x, y) ->
            result {
                let! vx = eval x
                let! vy = eval y
                if vx.Dims = vy.Dims then
                    return { Scale = vx.Scale - vy.Scale; Offset = vx.Offset; Dims = vx.Dims }
                else
                    return! Result.Error "Dimensional mismatch in subtraction"
            }
        | Mul(x, y) ->
            result {
                let! vx = eval x
                let! vy = eval y
                return { Scale = vx.Scale * vy.Scale; Offset = 0.0; Dims = addDims vx.Dims vy.Dims }
            }
        | Div(x, y) ->
            result {
                let! vx = eval x
                let! vy = eval y
                return { Scale = vx.Scale / vy.Scale; Offset = 0.0; Dims = subDims vx.Dims vy.Dims }
            }
        | Pow(x, y) ->
            result {
                let! vy = eval y
                
                if not (Map.isEmpty vy.Dims) then
                    return! Result.Error "Exponent must be dimensionless"
                else
                    let power = vy.Scale
                    let isInteger (v: float) = Math.Abs(v - Math.Round(v)) < 1e-6

                    let! (coeff, def) = 
                        match x with
                        | UnitCoeff(c, d) -> Result.Ok(c, d)
                        | _ -> 
                            result {
                                let! vx = eval x
                                return (1.0, vx)
                            }
                    
                    let newDims =
                        def.Dims
                        |> Map.map (fun _ v -> float v * power)
                        
                    let hasFractionalDims = 
                        newDims |> Map.exists (fun _ nv -> not (isInteger nv))
                        
                    if hasFractionalDims then
                        return! Result.Error "Fractional exponent resulted in non-integer dimensions"
                    else
                        let roundedDims = 
                            newDims 
                            |> Map.map (fun _ nv -> int (Math.Round(nv))) 
                            |> Map.filter (fun _ v -> v <> 0)
                            
                        return { Scale = coeff * (def.Scale ** power); Offset = 0.0; Dims = roundedDims }
            }
        | Func(name, args) ->
            result {
                let rec evalAll acc remaining =
                    match remaining with
                    | [] -> Result.Ok(List.rev acc)
                    | h :: t ->
                        result {
                            let! v = eval h
                            return! evalAll (v :: acc) t
                        }
                        
                let! vals = evalAll [] args
                let allDimensionless = vals |> List.forall (fun v -> Map.isEmpty v.Dims)

                match name.ToLower(), vals with
                | "log", [ baseVal; v ] when allDimensionless ->
                    return linear (Math.Log(v.Scale, baseVal.Scale)) Map.empty
                | ("log" | "log10"), [ v ] when allDimensionless -> return linear (Math.Log10(v.Scale)) Map.empty
                | "ln", [ v ] when allDimensionless -> return linear (Math.Log(v.Scale)) Map.empty
                | "sin", [ v ] when allDimensionless -> return linear (Math.Sin(v.Scale)) Map.empty
                | "cos", [ v ] when allDimensionless -> return linear (Math.Cos(v.Scale)) Map.empty
                | "tan", [ v ] when allDimensionless -> return linear (Math.Tan(v.Scale)) Map.empty
                | "sqrt", [ v ] ->
                    let hasOddPowers = v.Dims |> Map.exists (fun _ power -> power % 2 <> 0)
                    if hasOddPowers then
                        return! Result.Error "Cannot take the square root of an odd power dimension"
                    else
                        return linear (Math.Sqrt(v.Scale)) (v.Dims |> Map.map (fun _ p -> p / 2))
                | funcName, _ when
                    [ "log"; "log10"; "ln"; "sin"; "cos"; "tan" ] |> List.contains funcName
                    && not allDimensionless ->
                    return! Result.Error $"Arguments to {name} must be dimensionless"
                | _ -> return! Result.Error $"Unknown function or invalid argument count: {name}"
            }

    let opp = OperatorPrecedenceParser<Expr, unit, unit>()
    let pExpr = opp.ExpressionParser

    let pUnitStr = many1Chars (asciiLetter <|> digit <|> anyOf "_")

    let pTerm =
        (opt pfloat .>> spaces .>>. opt pUnitStr)
        >>= fun (numOpt, strOpt) ->
            let num = defaultArg numOpt 1.0

            match strOpt with
            | Some s ->
                match resolveSingleUnit s with
                | Some def -> preturn (UnitCoeff(num, def))
                | None -> fail $"Unknown unit or constant: '%s{s}'"
            | None ->
                if numOpt.IsSome then
                    preturn (Num num)
                else
                    fail "Expected number or unit"
    // Define what makes a valid function name
    let pFuncName =
        many1Satisfy2 (fun c -> isAsciiLetter c || c = '_') (fun  // First char: letter or underscore
                                                                 c -> isAsciiLetter c || isDigit c || c = '_') // Rest: letter, digit, or underscore

    // Parse a function
    let pFunc =
        pFuncName .>> spaces
        .>>. between (pstring "(" .>> spaces) (pstring ")" .>> spaces) (sepBy pExpr (pstring "," .>> spaces))
        |>> fun (name, args) -> Func(name, args)

    // Define a single "atomic" unit of the expression
    // Did I tell you I don't really understand fParsec?
    let pAtom =
        attempt pFunc
        <|> (pTerm .>> spaces)
        <|> between (pstring "(" .>> spaces) (pstring ")" .>> spaces) (pExpr |>> Group)

    // Check that the next character isn't an explicit math operator
    let pImplicitAtom = notFollowedBy (anyOf "+-*/^") >>. pAtom



    opp.TermParser <-
        (pTerm .>> spaces)
        <|> between (pstring "(" .>> spaces) (pstring ")" .>> spaces) (pExpr |>> Group)

    opp.AddOperator(InfixOperator("+", spaces, 1, Associativity.Left, fun x y -> Add(x, y)))
    opp.AddOperator(InfixOperator("-", spaces, 1, Associativity.Left, fun x y -> Sub(x, y)))
    opp.AddOperator(InfixOperator("*", spaces, 2, Associativity.Left, fun x y -> Mul(x, y)))
    opp.AddOperator(InfixOperator("/", spaces, 2, Associativity.Left, fun x y -> Div(x, y)))
    opp.AddOperator(InfixOperator("^", spaces, 3, Associativity.Right, fun x y -> Pow(x, y)))

    opp.TermParser <-
        pAtom .>>. many pImplicitAtom
        |>> fun (first, rest) -> List.fold (fun acc next -> Mul(acc, next)) first rest

    let parseAliasToDef (exprStr: string) =
        match run (spaces >>. pExpr .>> eof) exprStr with
        | Success(ast, _, _) ->
            match eval ast with
            | Result.Ok def -> def
            | Result.Error e -> failwithf $"Alias eval error '%s{exprStr}': %s{e}"
        | Failure(msg, _, _) -> failwithf $"Alias parse error '%s{exprStr}': %s{msg}"

    // --- 4. Database Initialization ---
    let private initializeDb () =
        let primitives =
            [ ("m", linear 1.0 L)
              ("s", linear 1.0 T)
              ("kg", linear 1.0 M)
              ("A", linear 1.0 I)
              ("b", linear 1.0 Info)
              ("K", linear 1.0 Temp)
              ("cd", linear 1.0 Cd)
              ("mol", linear 1.0 Mol)
              ]

        for (name, def) in primitives do
            unitDb[name] <- def
            
        unitDb["degC"] <-
            { Scale = 1.0
              Offset = 273.15
              Dims = Temp }

        unitDb["degF"] <-
            { Scale = 5.0 / 9.0
              Offset = 273.15 - (32.0 * 5.0 / 9.0)
              Dims = Temp }

        let aliases =
            [ // First the constants that are used further down
              ("pi", "3.141592653589793")
              ("c", "299792458 m / s")
              ("gn", "9.80665 m/s^2")
                
              
              
              ("min", "60 s")
              ("minute", "1 min")
              
              ("h", "60 min")
              ("hr", "1 h")
              ("hour", "1 h")
              
              ("d", "24 h")
              ("day", "1 d")
              
              ("wk", "7 d")
              ("week", "1 wk")
              
              ("yr", "31557600 s")
              ("year", "1 yr")

              // Length
              ("meter", "1 m")
              ("in", "0.0254 m")
              ("inch", "1 in")
              ("ft", "12 in")
              ("foot", "12 in")
              ("yd", "3 ft")
              
              ("yard", "1 yd")
              ("mi", "5280 ft")
              ("mile", "1 mi")
              ("miles", "1 mi")
              ("nmi", "1852 m")
              ("au", "149597870700 m")
              ("ly", "c * 1 yr")
              ("lightyear", "1 ly")
              ("pc", "3.085677581491367e16 m")
              
              // old length units
              ("fath", "6 ft"); ("fathom", "1 fath")           
              ("fur", "660 ft"); ("furlong", "1 fur")          

              // These are the base units for area. Units derived from these are solved at parse-time.
              // ie ft2 = ft^2
              // This will make all areas are volumes output in m2 and m3 unless
              // specified. This is fine.
              ("m2", "m^2")
              ("m3", "m^3")

              // Volume
              ("l", "0.001 m3"); ("litre", "1 l"); ("liter", "1 l")
              
              // non-metric
              ("gal", "3.78541 l"); ("gallon", "1 gal")
              ("qt", "0.25 gal"); ("quart", "1 qt")
              ("pt", "0.5 qt"); ("pint", "1 pt")
              ("fl_oz", "0.0625 pt"); ("fluid_ounce", "1 fl_oz")
              ("fl_dr", "1/8 fl_oz")
              ("gi", "4 fl_oz")
              
              // Cooking volume
              ("tsp", "5ml"); ("teaspoon", "1 tsp")
              ("tbsp", "15ml"); ("tablespoon", "1 tbsp")
              ("cup", "250 ml")
              
              // non-metric
              ("us_tsp", "4.92892159375 ml")
              ("us_tbsp", "3 us_tsp")
              ("us_cup", "16 us_tbsp")
              
              
              // area
              ("ha", "10000 m2"); ("hectare", "1 ha")
              ("are", "100m2")
              ("acre", "4046.8 m2")

              // mass
              ("g", "0.001 kg"); ("gram", "1 g")
              ("t", "1000 kg"); ("ton", "1 t"); ("tonne", "1 t");
              ("u", "1.66053906660e-27 kg")
              ("amu", "1 u")
              
              // non-metric
              ("slug", "14.5939029 kg")
              ("lb", "0.453592 kg"); ("pound", "1 lb")
              ("oz", "0.0625 lb"); ("ounce", "1 oz")
              ("gr", "1/7000 lb"); ("grain", "1 gr") 
              ("st", "14 lb"); ("stone", "1 st")
              ("short_ton", "2000 lb")
              ("long_ton", "2240 lb")

              // Pressure and force
              ("N", "kg * m / s^2"); ("newton", "1 N")
              ("lbf", "1 lb * gn")
              ("Pa", "N / m^2"); ("pascal", "1 Pa")
              ("bar", "100000 Pa")
              ("atm", "101325 Pa"); ("atmosphere", "1 atm")
              ("psi", "lbf / in2")
              ("mmHg", "133.322387415 Pa")
              ("dyn", "1e-5 N")
              ("kgf", "1 kg * gn")
              ("R", "2.58e-4 degC / kg")

              // Energy/work
              ("J", "N * m"); ("joule", "1 J")
              ("Nm", "1 J")
              ("W", "J / s"); ("Watt", "1 W")
              ("Wh", "W * h"); 
              ("hp", "745.6998715822702 W"); ("horsepower", "1 hp")
              ("cal", "4.184 J"); ("calorie", "1 cal")
              ("BTU", "1055.05585262 J")
              ("eV", "1.602176634e-19 J")
              ("erg", "1e-7 J")
                
              // electricity
              ("C", "A * s"); ("coulomb", "1 C")
              ("V", "W / A"); ("volt", "1 V")
              ("ohm", "V / A")
              ("F", "C / V"); ("farad", "1 F")
              ("H", "V * s / A"); ("henry", "1 H")
              ("Wb", "V * s"); ("weber", "1 Wb")
              ("tesla", "Wb / m2")
              ("T_tesla", "1 tesla")
              ("G", "1e-4 tesla")
              ("Mx", "1e-8 Wb")
              ("Oe", "79.57747 A / m")
              
              // Viscosity
              ("P", "0.1 Pa * s"); ("poise", "1 P")
              ("St", "1e-4 m2 / s"); ("stokes", "1 St")

              // IT
              ("B", "8 b"); ("byte", "1 B")

              // Photometry
              ("sr", "1") // Steradian (solid angle)
              ("lm", "cd * sr"); ("lumen", "1 lm") // Lumen (luminous flux)
              ("lx", "lm / m2"); ("lux", "1 lx") // Lux (illuminance)

              // Other constants
              ("e", "2.71828182846")
              ("planck", "6.62607015e-34 J * s")
              ("hbar", "planck / (2 * pi)")

              // Radioactivity
              ("Sv", "J / kg"); ("sievert", "1 Sv")
              ("Gy", "J / kg"); ("gray", "1 Gy")
              ("rem", "0.01 Sv")
              ("rad_dose", "0.01 Gy")
              ("Bq", "1 / s"); ("bequerel", "1 Bq")
              ("Ci", "3.7e10 Bq")

              // Angles & Rotation (Dimensionless)
              ("rad", "1")
              ("deg", "pi / 180")
              ("grad", "pi / 200")
              ("rev", "2 * pi")
              ("rpm", "rev / min")
              ("arcmin", "1 / 60 deg")
              ("arcsec", "1 / 3600 deg")
              

              // decibel, dimensionless
              ("dB", "1")

              // Dimensionless ratios
              ("%", "0.01")
              //Parts per ...
              ("ppm", "1e-6")
              ("ppb", "1e-9")
              ("ppt", "1e-12")
              
              // handy ones to avoid paretheses
              ("kph", "1 km/h")
              ("mph", "1 mi/h")
              ("bps", "1 b/s")
              ("Bps", "1 B/s")]

        for (name, exprStr) in aliases do
            unitDb[name] <- parseAliasToDef exprStr


    let _ = initializeDb ()
    //  Conversion Logic
    let formatNum (n: float) =
        n.ToString(formatString).Replace("−", "-")


    let findBestUnit (dims: Dims) (value: float) =
        let matchingUnits =
            unitDb
            |> Seq.filter (fun kvp -> kvp.Value.Dims = dims && kvp.Value.Offset = 0.0)
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value.Scale)
            |> Seq.toList

        if matchingUnits.IsEmpty then
            let dimStr =
                dims
                |> Map.toSeq
                |> Seq.map (fun (k, v) -> $"%s{k}^%d{v}")
                |> String.concat " * "

            $"%s{formatNum value} (%s{dimStr})"
        else
            let bestUnit =
                matchingUnits
                |> List.sortBy (fun (name, scale) -> (abs (scale - 1.0), name.Length))
                |> List.head
            let resultValue = value / (snd bestUnit)
            $"%s{formatNum resultValue} %s{fst bestUnit}"
    let private propRegex = Regex(@"([a-zA-Z_]\w*)\.([a-zA-Z_]\w*)", RegexOptions.Compiled)
    let rec resolveProperties (input: string) : string =
        let mutable current = input
        let mutable changed = true

        while changed do
            changed <- false

            current <-
                propRegex.Replace(
                    current,
                    MatchEvaluator(fun m ->
                        let entityName = m.Groups[1].Value
                        let propName = m.Groups[2].Value

                        match EntityParser.entities.TryGetValue(entityName) with
                        | true, props ->
                            match props.TryFind(propName) with
                            | Some value ->
                                changed <- true
                                value
                            | None -> m.Value
                        | false, _ -> m.Value)
                )

        current

    // Run macro and entity expansion until there is no
    // macro and unit expansion left
    let rec preprocess (input: string) =
        let afterMacros = MacroParser.expand input
        let afterEntities = resolveProperties afterMacros

        if afterEntities = input then
            afterEntities
        else
            preprocess afterEntities

    let private decFixedRegex = Regex(@"(?<=\d),(?=\d)", RegexOptions.Compiled)
    let private extractInRegex =Regex(@"(?i)\s+in\s+", RegexOptions.RightToLeft ||| RegexOptions.Compiled)
    let convertQuery (query: string) =
        // This recursively expands macros and entities until there is no more expansion
        // to be done. It can potentially loop forever.
        let fullyExpanded = preprocess query
        let decFixed = decFixedRegex.Replace(fullyExpanded.Trim(), ".")
        let parts = extractInRegex.Split(decFixed, 2)

        match parts with
        // Here we have a LHS with an expression and a RHS that is on the right side of the "in"
        | [| lhs; rhs |] ->
            let leftStr = lhs.Replace(",", " + ")
            let rightStrs = rhs.Split(',') |> Array.map (fun s -> s.Trim())

            match run (spaces >>. pExpr .>> eof) leftStr with
            | Success(leftAst, _, _) ->
                match eval leftAst with
                | Result.Ok leftDef ->
                    let rightResults =
                        rightStrs
                        |> Array.map (fun s ->
                            match run (spaces >>. pExpr .>> eof) s with
                            | Success(ast, _, _) -> eval ast
                            | Failure(msg, _, _) -> Result.Error $"Syntax Error in target '%s{s}': %s{msg}")

                    let hasErrors =
                        rightResults
                        |> Array.tryPick (function
                            | Result.Error e -> Some e
                            | _ -> None)

                    match hasErrors with
                    | Some e -> $"Math Error (Right): %s{e}"
                    | None ->
                        let targetDefs =
                            rightResults
                            |> Array.choose (function
                                | Result.Ok d -> Some d
                                | _ -> None)

                        if targetDefs.Length = 1 then
                            let rightDef = targetDefs[0]

                            if leftDef.Dims = rightDef.Dims then
                                let baseVal = leftDef.Scale + leftDef.Offset
                                let result = (baseVal - rightDef.Offset) / rightDef.Scale
                                $"%s{formatNum result} %s{rightStrs[0]}"
                            else
                                let mulDims = addDims leftDef.Dims rightDef.Dims
                                let divDims = subDims leftDef.Dims rightDef.Dims

                                let countDims d =
                                    d |> Map.toSeq |> Seq.sumBy (fun (_, v) -> abs v)

                                let resultSI, resultDims =
                                    if countDims mulDims < countDims divDims then
                                        leftDef.Scale * rightDef.Scale, mulDims
                                    else
                                        leftDef.Scale / rightDef.Scale, divDims

                                if Map.isEmpty resultDims then
                                    $"%s{formatNum resultSI} (Dimensionless multiplier)"
                                else
                                    findBestUnit resultDims resultSI
                        else
                            let allDimsMatch = targetDefs |> Array.forall (fun t -> t.Dims = leftDef.Dims)

                            if not allDimsMatch then
                                "Error: All output units must exactly match the input dimensions."
                            elif leftDef.Dims = Temp then
                                "Error: Mixed outputs not supported for temperatures."
                            else
                                let mutable remaining = leftDef.Scale
                                let outputParts = ResizeArray<string>()

                                for i = 0 to targetDefs.Length - 1 do
                                    let tDef = targetDefs[i]
                                    let tStr = rightStrs[i]

                                    if i < targetDefs.Length - 1 then
                                        let rounded = Math.Round(remaining / tDef.Scale, 9)
                                        let wholePart = Math.Truncate(rounded)

                                        if wholePart <> 0.0 then
                                            outputParts.Add $"%g{wholePart} %s{tStr}"

                                        remaining <- remaining - (wholePart * tDef.Scale)
                                    else
                                        let valueInUnit = remaining / tDef.Scale

                                        if Math.Abs(valueInUnit) > 1e-9 || outputParts.Count = 0 then
                                            outputParts.Add $"%s{formatNum valueInUnit} %s{tStr}"

                                String.Join(", ", outputParts)

                | Result.Error e -> $"Math Error (Left): %s{e}"
            | Failure(msg, _, _) -> $"Syntax Error (Left): %s{msg}"
        // no "in", so we just reduce it.
        | [| expr |] ->
            match run (spaces >>. pExpr .>> eof) expr with
            | Success(leftAst, _, _) ->
                match eval leftAst with
                | Result.Ok leftDef ->
                    // Auto-simplify the left side since there are no target units
                    if Map.isEmpty leftDef.Dims then
                        formatNum leftDef.Scale
                    else
                        findBestUnit leftDef.Dims leftDef.Scale
                | Result.Error e -> $"Math Error: %s{e}"
            | Failure(msg, _, _) -> $"Syntax Error: %s{msg}"

        | _ -> "Expression error. Too many \"in\"s?"

    let clearMacrosAndEntities () =
        MacroParser.macros.Clear()
        EntityParser.clearEntities ()
