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
    open EntityParser
    open MacroParser
    // --- 1. Core Dimensions ---
    type Dims = Map<string, int>

    let L = Map ["L", 1] // Length
    let M = Map ["M", 1] //mass
    let T = Map ["T", 1] //time
    let Temp = Map ["Temp", 1] //temp
    let Info = Map ["Info", 1] //IT
    let I = Map ["I", 1] // current
    let Cd = Map ["Cd", 1] // photometry

    let addDims (d1: Dims) (d2: Dims) : Dims =
        let keys = Seq.append (Map.keys d1) (Map.keys d2) |> Seq.distinct
        keys 
        |> Seq.map (fun k -> k, (Map.tryFind k d1 |> Option.defaultValue 0) + (Map.tryFind k d2 |> Option.defaultValue 0))
        |> Seq.filter (fun (_, v) -> v <> 0)
        |> Map.ofSeq

    let subDims (d1: Dims) (d2: Dims) : Dims =
        let keys = Seq.append (Map.keys d1) (Map.keys d2) |> Seq.distinct
        keys 
        |> Seq.map (fun k -> k, (Map.tryFind k d1 |> Option.defaultValue 0) - (Map.tryFind k d2 |> Option.defaultValue 0))
        |> Seq.filter (fun (_, v) -> v <> 0)
        |> Map.ofSeq

    // --- 2. UnitDef & Database ---
    type UnitDef = { Scale: float; Offset: float; Dims: Dims }
    let linear scale dims = { Scale = scale; Offset = 0.0; Dims = dims }

    let prefixes = 
        [
            ("Q", 1e30); ("R", 1e27); ("Y", 1e24); ("Z", 1e21)
            ("E", 1e18); ("P", 1e15); ("T", 1e12); ("G", 1e9)
            ("M", 1e6); ("k", 1e3); ("h", 1e2); ("da", 1e1)
            ("d", 1e-1); ("c", 1e-2); ("m", 1e-3); ("u", 1e-6)
            ("n", 1e-9); ("p", 1e-12); ("f", 1e-15); ("a", 1e-18)
            ("z", 1e-21); ("y", 1e-24); ("r", 1e-27); ("q", 1e-30)
            ("Yi", 1208925819614629174706176.0)
            ("Zi", 1180591620717411303424.0)
            ("Ei", 1152921504606846976.0)
            ("Pi", 1125899906842624.0)
            ("Ti", 1099511627776.0)
            ("Gi", 1073741824.0)
            ("Mi", 1048576.0)
            ("Ki", 1024.0); ("ki", 1024.0)
        ] |> List.sortByDescending (fun (p, _) -> p.Length)

    let unitDb = Dictionary<string, UnitDef>()

    let rec resolveSingleUnit (u: string) =
        if unitDb.ContainsKey(u) then Some unitDb[u]
        else
            // Check if the string ends with a number (e.g., "in2", "cm3")
            let powerMatch = Regex.Match(u, @"^([a-zA-Z_]+)(\d+)$")
            if powerMatch.Success then
                let baseStr = powerMatch.Groups[1].Value
                let power = float powerMatch.Groups[2].Value
                
                // Recursively resolve the base unit and apply the exponent
                match resolveSingleUnit baseStr with
                | Some b ->
                    let newDims = 
                        b.Dims 
                        |> Map.map (fun _ v -> v * int power) 
                        |> Map.filter (fun _ v -> v <> 0)
                    Some { Scale = b.Scale ** power; Offset = 0.0; Dims = newDims }
                | None -> None
            else
                // Fallback to checking for SI prefixes
                match prefixes |> List.tryFind (fun (p, _) -> u.StartsWith(p)) with
                | Some (p, mult) ->
                    let baseUnit = u.Substring(p.Length)
                    match resolveSingleUnit baseUnit with
                    | Some b -> Some { Scale = b.Scale * mult; Offset = b.Offset; Dims = b.Dims }
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

    let rec eval expr =
        match expr with
        | Num n -> Result.Ok (linear n Map.empty)
        | UnitCoeff(coeff, def) -> Result.Ok { Scale = coeff * def.Scale; Offset = def.Offset; Dims = def.Dims }
        | Group e -> eval e
        | Add(x, y) -> 
            match eval x, eval y with
            | Result.Ok vx, Result.Ok vy -> 
                if vx.Dims = vy.Dims then Result.Ok { Scale = vx.Scale + vy.Scale; Offset = vx.Offset; Dims = vx.Dims }
                else Result.Error "Dimensional mismatch in addition"
            | Result.Error e, _ | _, Result.Error e -> Result.Error e
        | Sub(x, y) -> 
            match eval x, eval y with
            | Result.Ok vx, Result.Ok vy -> 
                if vx.Dims = vy.Dims then Result.Ok { Scale = vx.Scale - vy.Scale; Offset = vx.Offset; Dims = vx.Dims }
                else Result.Error "Dimensional mismatch in subtraction"
            | Result.Error e, _ | _, Result.Error e -> Result.Error e
        | Mul(x, y) -> 
            match eval x, eval y with
            | Result.Ok vx, Result.Ok vy -> Result.Ok { Scale = vx.Scale * vy.Scale; Offset = 0.0; Dims = addDims vx.Dims vy.Dims }
            | Result.Error e, _ | _, Result.Error e -> Result.Error e
        | Div(x, y) -> 
            match eval x, eval y with
            | Result.Ok vx, Result.Ok vy -> Result.Ok { Scale = vx.Scale / vy.Scale; Offset = 0.0; Dims = subDims vx.Dims vy.Dims }
            | Result.Error e, _ | _, Result.Error e -> Result.Error e
        | Pow(x, y) ->
            let applyPower coeff (def: UnitDef) (vy: UnitDef) =
                if not (Map.isEmpty vy.Dims) then Result.Error "Exponent must be dimensionless"
                else
                    let power = vy.Scale
                    let mutable isValid = true
                    let isInteger (v: float) = Math.Abs(v - Math.Round(v)) < 1e-6 
                    let newDims = 
                        def.Dims 
                        |> Map.map (fun _ v -> 
                            let nv = float v * power
                            if not (isInteger nv) then isValid <- false
                            int (Math.Round(nv)))
                        |> Map.filter (fun _ v -> v <> 0)
                    
                    if not isValid then Result.Error "Fractional exponent resulted in non-integer dimensions"
                    else Result.Ok { Scale = coeff * (def.Scale ** power); Offset = 0.0; Dims = newDims }

            match x, eval y with
            | UnitCoeff(coeff, def), Result.Ok vy -> applyPower coeff def vy
            | _, Result.Ok vy ->
                match eval x with
                | Result.Ok vx -> applyPower 1.0 vx vy
                | Result.Error e -> Result.Error e
            | _, Result.Error e -> Result.Error e
        | Func(name, args) ->
            // Helper to evaluate all arguments and fail fast on the first error
            let rec evalAll acc remaining =
                match remaining with
                | [] -> Result.Ok (List.rev acc)
                | h::t -> 
                    match eval h with
                    | Result.Ok v -> evalAll (v::acc) t
                    | Result.Error e -> Result.Error e
                    
            match evalAll [] args with
            | Result.Error e -> Result.Error e
            | Result.Ok vals ->
                let allDimensionless = vals |> List.forall (fun v -> Map.isEmpty v.Dims)
                
                match name.ToLower(), vals with
                // 2-Argument Logarithm: log(base, value)
                | "log", [baseVal; v] when allDimensionless ->
                    Result.Ok (linear (Math.Log(v.Scale, baseVal.Scale)) Map.empty)
                
                // 1-Argument Functions
                | ("log" | "log10"), [v] when allDimensionless -> 
                    Result.Ok (linear (Math.Log10(v.Scale)) Map.empty)
                | "ln", [v] when allDimensionless -> 
                    Result.Ok (linear (Math.Log(v.Scale)) Map.empty)
                | "sin", [v] when allDimensionless -> 
                    Result.Ok (linear (Math.Sin(v.Scale)) Map.empty)
                | "cos", [v] when allDimensionless -> 
                    Result.Ok (linear (Math.Cos(v.Scale)) Map.empty)
                | "tan", [v] when allDimensionless -> 
                    Result.Ok (linear (Math.Tan(v.Scale)) Map.empty)
                
                // Functions that scale dimensions mathematically
                | "sqrt", [v] -> 
                    let hasOddPowers = v.Dims |> Map.exists (fun _ power -> power % 2 <> 0)
                    if hasOddPowers then Result.Error "Cannot take the square root of an odd power dimension"
                    else Result.Ok (linear (Math.Sqrt(v.Scale)) (v.Dims |> Map.map (fun _ p -> p / 2)))
                
                // Error Fallbacks
                | funcName, _ when ["log"; "log10"; "ln"; "sin"; "cos"; "tan"] |> List.contains funcName && not allDimensionless ->
                    Result.Error $"Arguments to '%s{name}' must be dimensionless"
                | _ -> Result.Error $"Unknown function or invalid argument count: %s{name}"
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
                if numOpt.IsSome then preturn (Num num)
                else fail "Expected number or unit"
    // Define what makes a valid function name
    let pFuncName =
        many1Satisfy2 
            (fun c -> isAsciiLetter c || c = '_')                   // First char: letter or underscore
            (fun c -> isAsciiLetter c || isDigit c || c = '_')      // Rest: letter, digit, or underscore

    // Parse a function
    let pFunc =
        pFuncName .>> spaces 
        .>>. between 
                (pstring "(" .>> spaces) 
                (pstring ")" .>> spaces) 
                (sepBy pExpr (pstring "," .>> spaces))
        |>> fun (name, args) -> Func(name, args)

    opp.TermParser <- (pTerm .>> spaces) <|> between (pstring "(" .>> spaces) (pstring ")" .>> spaces) (pExpr |>> Group)

    opp.AddOperator(InfixOperator("+", spaces, 1, Associativity.Left, fun x y -> Add(x, y)))
    opp.AddOperator(InfixOperator("-", spaces, 1, Associativity.Left, fun x y -> Sub(x, y)))
    opp.AddOperator(InfixOperator("*", spaces, 2, Associativity.Left, fun x y -> Mul(x, y)))
    opp.AddOperator(InfixOperator("/", spaces, 2, Associativity.Left, fun x y -> Div(x, y)))
    opp.AddOperator(InfixOperator("^", spaces, 3, Associativity.Right, fun x y -> Pow(x, y)))
    opp.TermParser <- 
        attempt pFunc 
        <|> (pTerm .>> spaces) 
        <|> between (pstring "(" .>> spaces) (pstring ")" .>> spaces) (pExpr |>> Group)

    let parseAliasToDef (exprStr: string) =
        match run (spaces >>. pExpr .>> eof) exprStr with
        | Success(ast, _, _) ->
            match eval ast with
            | Result.Ok def -> def
            | Result.Error e -> failwithf $"Alias eval error '%s{exprStr}': %s{e}"
        | Failure(msg, _, _) -> failwithf $"Alias parse error '%s{exprStr}': %s{msg}"

    // --- 4. Database Initialization ---
    let private initializeDb () =
        let primitives = [
            ("m", linear 1.0 L)
            ("s", linear 1.0 T); 
            ("kg", linear 1.0 M)
            ("A", linear 1.0 I)
            ("b", linear 1.0 Info)
            ("K", linear 1.0 Temp)
            ("cd", linear 1.0 Cd)
            
            ("gn", linear 9.80665 (subDims L (addDims T T)))
            ("pi", linear Math.PI Map.empty)
            ("c", linear 299792458.0 (subDims L T))
        ]
        for (name, def) in primitives do unitDb[name] <- def

        let aliases = [
            ("min", "60 s")
            ("h", "60 min"); ("hr", "1 h")
            ("d", "24 h"); ("day", "1 d")
            ("wk", "7 d"); ("week", "1 wk")
            ("yr", "31557600 s"); ("year", "1 yr")
            
            // Length
            ("in", "0.0254 m")
            ("ft", "12 in")
            ("yd", "3 ft")
            ("mi", "5280 ft")
            ("nmi", "1852 m")
            ("au", "149597870700 m")
            ("ly", "c * 1 yr")
            ("pc", "3.085677581491367e16 m")
            
            // These are the base units for area. Units derived from these are solved at parse-time.
            // ie ft2 = ft^2
            // This will make all areas are volumes output in m2 and m3 unless
            // specified. This is fine. 
            ("m2", "m^2")
            ("m3", "m^3")
            
            // Volume
            ("l", "0.001 m3")
            ("gal", "3.78541 l")
            ("qt", "0.25 gal")
            ("pt", "0.5 qt")
            ("fl_oz", "0.0625 pt")
            
            // area
            ("ha", "10000 m2")
            ("are", "100m2")
            ("acre", "4046.8 m2")
            
            ("g", "0.001 kg")
            ("lb", "0.453592 kg")
            ("oz", "0.0625 lb")
            ("ton", "1000 kg")
            ("u", "1.66053906660e-27 kg")
            ("amu", "1 u")
            
            ("N", "kg * m / s^2")
            ("lbf", "1 lb * gn")
            ("Pa", "N / m^2")
            ("bar", "100000 Pa")
            ("atm", "101325 Pa")
            ("psi", "lbf / in2")
            
            ("J", "N * m")
            ("W", "J / s")
            ("Wh", "W * h")
            ("hp", "745.6998715822702 W")
            ("cal", "4.184 J")
            ("BTU", "1055.05585262 J")
            ("eV", "1.602176634e-19 J")

            ("C", "A * s")
            ("V", "W / A")
            ("ohm", "V / A")
            ("F", "C / V")
            ("H", "V * s / A")
            ("Wb", "V * s")
            ("tesla", "Wb / m2")
            ("T_tesla", "1 tesla")
            
            // IT
            ("B", "8 b")
            
            // Photometry
            ("sr", "1")          // Steradian (solid angle)
            ("lm", "cd * sr")    // Lumen (luminous flux)
            ("lx", "lm / m2")    // Lux (illuminance)
            
            // Constants
            ("planck", "6.62607015e-34 J * s")
            ("hbar", "planck / (2 * pi)")
            
             // Radioactivity
            ("Sv", "J / kg")
            ("Gy", "J / kg")
            ("rem", "0.01 Sv")
            ("rad_dose", "0.01 Gy")
            ("Bq", "1 / s")
            ("Ci", "3.7e10 Bq")
            ("R", "2.58e-4 C / kg")
             
             // Angles & Rotation (Dimensionless)
            ("rad", "1")
            ("deg", "pi / 180")
            ("rev", "2 * pi")
            ("rpm", "rev / min")
            
            // decibel, dimensionless
            ("dB", "1")
            
            // Dimensionless ratios
            ("%", "0.01")
            //Parts per ...
            ("ppm", "1e-6") 
            ("ppb", "1e-9")
            ("ppt", "1e-12")
            
            // Molar Masses (Mass of 1 mole of specific substances)
            ("mol_water", "18.015 g")
            ("mol_nacl", "58.44 g")
            ("mol_ethanol", "46.07 g")
            
            // Common Blood Panel Substances
            ("mol_glucose", "180.156 g")
            ("mol_cholesterol", "386.654 g")
            ("mol_triglycerides", "885.43 g")
            ("mol_urea", "60.056 g")
        ]
        
        for (name, exprStr) in aliases do
            unitDb[name] <- parseAliasToDef exprStr

        unitDb["degC"] <- { Scale = 1.0; Offset = 273.15; Dims = Temp }
        unitDb["degF"] <- { Scale = 5.0/9.0; Offset = 273.15 - (32.0 * 5.0/9.0); Dims = Temp }

    let _ = 
        initializeDb ()
    //  Conversion Logic
    let formatNum (n: float) =
        n.ToString("G7").Replace("−", "-")


    let findBestUnit (dims: Dims) (value: float) =
        let matchingUnits = 
            unitDb 
            |> Seq.filter (fun kvp -> kvp.Value.Dims = dims && kvp.Value.Offset = 0.0)
            |> Seq.map (fun kvp -> kvp.Key, kvp.Value.Scale)
            |> Seq.toList
    
        if matchingUnits.IsEmpty then
            let dimStr = dims |> Map.toSeq |> Seq.map (fun (k, v) -> $"%s{k}^%d{v}") |> String.concat " * "
            $"%s{formatNum value} (%s{dimStr})"
        else
            let bestUnit = 
                matchingUnits
                |> List.sortBy (fun (name, scale) -> (abs (scale - 1.0), name.Length))
                |> List.head
                // |> List.minBy (fun (_, scale) -> 
                //     let v = Math.Abs(value / scale)
                //     if v >= 1.0 then v else 1.0 / v)
            let resultValue = value / (snd bestUnit)
            $"%s{formatNum resultValue} %s{fst bestUnit}"

    let rec resolveProperties (input: string) : string =
        let propRegex = Regex(@"([a-zA-Z_]\w*)\.([a-zA-Z_]\w*)")
        let mutable current = input
        let mutable changed = true
        let mutable pass = 1

        while changed do
            changed <- false
            current <- propRegex.Replace(current, MatchEvaluator(fun m ->
                let entityName = m.Groups[1].Value
                let propName = m.Groups[2].Value

                match EntityParser.entities.TryGetValue(entityName) with
                | true, props ->
                    match props.TryFind(propName) with
                    | Some value ->
                        changed <- true
                        value // Replaces "earth.mass" with "(5.972*10^24)kg"
                    | None ->
                        m.Value
                | false, _ ->
                    m.Value
            ))
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


    let convertQuery (query: string) =
        // This recursively expands macros and entities until there is no more expansion
        // to be done. It can potentially loop forever.
        let fullyExpanded = preprocess query
        printf $"%s{fullyExpanded}"
        let decFixed = Regex.Replace(fullyExpanded.Trim(), @"(?<=\d),(?=\d)", ".")
        let parts = Regex.Split(decFixed, @"(?i)\s+in\s+")
        
        match parts with
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
                    
                    let hasErrors = rightResults |> Array.tryPick (function Result.Error e -> Some e | _ -> None)
                    match hasErrors with
                    | Some e -> $"Math Error (Right): %s{e}"
                    | None ->
                        let targetDefs = rightResults |> Array.choose (function Result.Ok d -> Some d | _ -> None)
                        
                        if targetDefs.Length = 1 then
                            let rightDef = targetDefs[0]
                            if leftDef.Dims = rightDef.Dims then
                                let baseVal = leftDef.Scale + leftDef.Offset
                                let result = (baseVal - rightDef.Offset) / rightDef.Scale
                                $"%s{formatNum result} %s{rightStrs[0]}"
                            else
                                let mulDims = addDims leftDef.Dims rightDef.Dims
                                let divDims = subDims leftDef.Dims rightDef.Dims
                                let countDims d = d |> Map.toSeq |> Seq.sumBy (fun (_, v) -> abs v)

                                let resultSI, resultDims = 
                                    if countDims mulDims < countDims divDims then leftDef.Scale * rightDef.Scale, mulDims
                                    else leftDef.Scale / rightDef.Scale, divDims
                                
                                if Map.isEmpty resultDims then $"%s{formatNum resultSI} (Dimensionless multiplier)"
                                else findBestUnit resultDims resultSI
                        else
                            let allDimsMatch = targetDefs |> Array.forall (fun t -> t.Dims = leftDef.Dims)
                            if not allDimsMatch then "Error: All output units must exactly match the input dimensions."
                            elif leftDef.Dims = Temp then "Error: Mixed outputs not supported for temperatures."
                            else
                                let mutable remaining = leftDef.Scale
                                let outputParts = ResizeArray<string>()
                                
                                for i = 0 to targetDefs.Length - 1 do
                                    let tDef = targetDefs[i]
                                    let tStr = rightStrs[i]
                                    
                                    if i < targetDefs.Length - 1 then
                                        let rounded = Math.Round(remaining / tDef.Scale, 9)
                                        let wholePart = Math.Truncate(rounded)
                                        if wholePart <> 0.0 then outputParts.Add $"%g{wholePart} %s{tStr}"
                                        remaining <- remaining - (wholePart * tDef.Scale)
                                    else
                                        let valueInUnit = remaining / tDef.Scale
                                        if Math.Abs(valueInUnit) > 1e-9 || outputParts.Count = 0 then
                                            outputParts.Add $"%s{formatNum valueInUnit} %s{tStr}"

                                String.Join(", ", outputParts)
                                
                | Result.Error e -> $"Math Error (Left): %s{e}"
            | Failure(msg, _, _) -> $"Syntax Error (Left): %s{msg}"
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
