namespace Umits

open System
open System.Text.RegularExpressions
open System.Collections.Generic
(*
They follow a simple syntax. A template entity starts with a %. Properties from the template
is simply copied verbatim into the entity. Properties are written inside curly braces as such:

hydrogen { molar_mass=xyzg speed_of_sound=23442furlongs/fortnight }
 
 A $ is replaced by the name of the entity, and is
expanded in the entity body, and not in any templates. So a template %cubish { volume=$.width*$.length*$.height }

We can then define proper_2m_cube(cubish) { width=2m height=2m length=2m } and volume is added for us. 

Currently, this uses Regex. I'm sorry. I had to fight Parsec a bit too much since I have no idea what I'm doing.
 *)

module EntityParser =
    let templates = Dictionary<string, string>()
    let entities = Dictionary<string, Map<string, string>>()
    
    let clearEntities () =
        templates.Clear()
        entities.Clear()

    let parseFiles (fileContents: string seq) =
        
        let fullText = String.concat "\n" fileContents
        // The regex to read entity names and {}-blocks
        let blockRegex = Regex(@"(%?[a-zA-Z_]\w*)\s*(?:\(([a-zA-Z_]\w*)\))?\s*\{([^}]*)\}", RegexOptions.Multiline)
        // The block contents.
        let kvRegex = Regex(@"([a-zA-Z_]\w*)\s*=\s*(?:""([^""]*)""|(\S+))")

        for m in blockRegex.Matches(fullText) do
            let name = m.Groups[1].Value
            let parent = if m.Groups[2].Success then m.Groups[2].Value else ""
            let rawBody = m.Groups[3].Value

            // mutsble becayse we add any templatw values to it next.
            let mutable mergedBody = rawBody
            
            // I should probably print some error message if the parent does not exist.
            if parent <> "" && templates.ContainsKey("%" + parent) then
                mergedBody <- templates["%" + parent] + "\n" + mergedBody

            // We don't want to expand $ in templates
            if name.StartsWith("%") then
                templates[name] <- mergedBody
            else
                // SNow we expand $. to the actual entity name.
                let expandedBody = mergedBody.Replace("$.", name + ".")
                
                let kvs = 
                    kvRegex.Matches(expandedBody)
                    |> Seq.cast<Match>
                    |> Seq.map (fun kv -> 
                        let k = kv.Groups[1].Value
                        let v = if kv.Groups[2].Success then kv.Groups[2].Value else kv.Groups[3].Value
                        k, v)
                    |> Map.ofSeq
                entities[name] <- kvs
                
module MacroParser =

    // Matches definitions in macros.txt: name(arg1, arg2) = body
    let private defRegex = Regex(@"^([a-zA-Z_]\w*)\(([^)]+)\)\s*=\s*(.+)$", RegexOptions.Compiled)
    
    // Matches usage in queries using .NET balancing groups for nested parentheses
    let private useRegex = Regex(@"([a-zA-Z_]\w*)\(((?>[^()]+|\((?<DEPTH>)|\)(?<-DEPTH>))*(?(DEPTH)(?!)))\)", RegexOptions.Compiled)

    let macros = Dictionary<string, string[] * string>()

    // Safely splits arguments on commas, ignoring commas inside nested parentheses
    let private splitArgs (argString: string) =
        let mutable depth = 0
        let mutable current = ""
        let args = ResizeArray<string>()
        
        for c in argString do
            match c with
            | '(' -> 
                depth <- depth + 1
                current <- current + string c
            | ')' -> 
                depth <- depth - 1
                current <- current + string c
            | ',' when depth = 0 -> 
                args.Add(current.Trim())
                current <- ""
            | _ -> 
                current <- current + string c
                
        args.Add(current.Trim())
        args.ToArray()

    let parseFile (content: string) =
        let lines = content.Split([|'\r'; '\n'|], System.StringSplitOptions.RemoveEmptyEntries)
        for line in lines do
            if not (line.StartsWith("//")) then
                let m = defRegex.Match(line)
                if m.Success then
                    let name = m.Groups[1].Value
                    let args = m.Groups[2].Value.Split(',') |> Array.map (fun s -> s.Trim())
                    let body = m.Groups[3].Value.Trim()
                    macros[name] <- (args, body)

    let rec expand (input: string) : string =
        let mutable current = input
        let mutable changed = true
        
        while changed do
            changed <- false
            current <- useRegex.Replace(current, MatchEvaluator(fun m ->
                let name = m.Groups[1].Value
                let argString = m.Groups[2].Value 
                let args = splitArgs argString    
                
                match macros.TryGetValue(name) with
                | true, (defArgs, body) when args.Length = defArgs.Length ->
                    changed <- true
                    let mutable expanded = body
                    for i in 0 .. args.Length - 1 do
                        expanded <- expanded.Replace("{" + defArgs[i] + "}", "(" + args[i] + ")")
                    "(" + expanded + ")" 
                | _ ->
                    // If it is not a macro, we expand whatever is inside the
                    // parentheses
                    let innerExpanded = expand argString
                    if innerExpanded <> argString then
                        changed <- true
                    name + "(" + innerExpanded + ")"
            ))
        current


