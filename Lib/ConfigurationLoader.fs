namespace Umits

module ConfigurationLoader =
    open System.IO
    open System.Reflection

    let private readResource (name: string) =
        let assembly = Assembly.GetExecutingAssembly()
        let resourceName = $"Lib.%s{name}" 
        
        use stream = assembly.GetManifestResourceStream(resourceName)
        if stream = null then 
            failwith $"Embedded resource %s{resourceName} not found."
            
        use reader = new StreamReader(stream)
        reader.ReadToEnd()

    let loadAll () =
        let macroContent = readResource "macros.txt"
        MacroParser.parseFile macroContent

        // read entities files from index.txt
        let indexContent = readResource "Entities.index.txt"
        let fileNames = indexContent.Split([|'\r'; '\n'|], System.StringSplitOptions.RemoveEmptyEntries)
        
        let entityContents = 
            fileNames
            |> Array.map (fun fileName -> readResource $"Entities.%s{fileName.Trim()}")
            
        EntityParser.parseFiles entityContents

