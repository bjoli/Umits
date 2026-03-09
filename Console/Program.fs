open System
open System.Text.RegularExpressions
open Umits

let processInput (rawInput: string) (history: Map<int, string>) (lastResult: string option) =
    // 1. Handle implicit chaining if the query starts with "in "
    let chainedInput = 
        if rawInput.StartsWith("in ", StringComparison.OrdinalIgnoreCase) then
            match lastResult with
            | Some res -> $"%s{res} %s{rawInput}"
            | None -> rawInput
        else
            rawInput

    // 2. Expand $N variables
    let evaluator (m: Match) =
        let index = int m.Groups[1].Value
        match Map.tryFind index history with
        | Some value -> value
        | None -> m.Value // Leave untouched if the index doesn't exist

    Regex.Replace(chainedInput, @"\$(\d+)", MatchEvaluator(evaluator))

let rec repl (historyIdx: int) (history: Map<int, string>) (lastResult: string option) =
    printf "> "
    let input = Console.ReadLine()
    
    if isNull input || input.Trim().ToLower() = "exit" || input.Trim().ToLower() = "quit" then
        () // Exit loop
    elif String.IsNullOrWhiteSpace(input) then
        repl historyIdx history lastResult
    else
        let query = processInput (input.Trim()) history lastResult
        let result = Engine.convertQuery query
        
        // Don't bind errors to the history variables
        if result.Contains("Error") then
            printfn $"%s{result}"
            repl historyIdx history lastResult
        else
            printfn $"$%d{historyIdx} = %s{result}"
            let newHistory = Map.add historyIdx result history
            repl (historyIdx + 1) newHistory (Some result)

[<EntryPoint>]
let main _ =
    Umits.ConfigurationLoader.loadAll()
    printfn "Umits REPL. Type 'exit' to quit."
    repl 0 Map.empty None
    0

