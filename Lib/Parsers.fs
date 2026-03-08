namespace Umits

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

    let parseFiles (fileContents: string seq) =
        templates.Clear()
        entities.Clear()
        
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
    // Maps MacroName -> (ArgumentArray, TemplateString)
    let macros = Dictionary<string, string[] * string>()

    let parseFile (content: string) =
        let defRegex = Regex(@"^([a-zA-Z_]\w*)\[([^\]]+)\]\s*=\s*(.+)$", RegexOptions.Multiline)
        for m in defRegex.Matches(content) do
            let name = m.Groups[1].Value
            let args = m.Groups[2].Value.Split(',') |> Array.map (fun s -> s.Trim())
            let body = m.Groups[3].Value.Trim()
            macros[name] <- (args, body)

    let rec expand (input: string) : string =
        // Regex with balancing groups to match outer macro. I did not write this.
        // someone who knows regex did.
        let outerMacroRegex = Regex(@"([a-zA-Z_]\w*)\[((?:[^\[\]]|(?<open>\[)|(?<-open>\]))+(?(open)(?!)))\]")
        let m = outerMacroRegex.Match(input)
        
        if m.Success then
            let macroName = m.Groups[1].Value
            let argsStr = m.Groups[2].Value
            
            match macros.TryGetValue(macroName) with
            | true, (argNames, template) ->
                // Split arguments. A sinple string split is not great, but it's my project. 
                let providedArgs = argsStr.Split(',') |> Array.map (fun s -> s.Trim())
                
                let mutable expandedBody = template
                for i = 0 to min (argNames.Length - 1) (providedArgs.Length - 1) do
                    expandedBody <- expandedBody.Replace($"{{{argNames[i]}}}", providedArgs[i])
                
                // Wrap in parens and replace in the original string
                let replacedStr = 
                    input.Substring(0, m.Index) + 
                    "(" + expandedBody + ")" + 
                    input.Substring(m.Index + m.Length)
                
                // Recursively evaluate to handle macros outputting macros (Out -> In)
                expand replacedStr
            | false, _ -> 
                // Macro not found, return as is and let soneone else handle ir
                input
        else
            input

