(*---------------------------------------------------------------------------
    Copyright 2013 Microsoft

    Licensed under the Apache License, Version 2.0 (the "License");
    you may not use this file except in compliance with the License.
    You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

    Unless required by applicable law or agreed to in writing, software
    distributed under the License is distributed on an "AS IS" BASIS,
    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
    See the License for the specific language governing permissions and
    limitations under the License.                                                      

    File: 
        Utils.fs
  
    Description: 
        Utilities
---------------------------------------------------------------------------*)
namespace Prajna.Examples.Common

open System
open System.Reflection
open System.IO

open Prajna.Tools
open Prajna.Core

/// Utilities that can be used by different examples projects
module Utility =

    let GetExecutingDir() =
        let codeBase = Assembly.GetExecutingAssembly().CodeBase
        let uri = UriBuilder(codeBase)
        let path = Uri.UnescapeDataString(uri.Path)
        Path.GetDirectoryName(path)

    /// Get examples under a namespace
    let GetExamples (assembly : Assembly, ``namespace`` : string) =
        let t = typeof<IExample>
        assembly.GetTypes()
        |> Array.filter(fun x -> t.IsAssignableFrom(x) && not x.IsInterface && not x.IsAbstract && x.Namespace = ``namespace``)
        |> Array.map(fun x -> (Activator.CreateInstance(x)) :?> IExample)    

    /// Get examples within an assembly
    let GetExamplesFromAssembly (assembly : Assembly) =
        let t = typeof<IExample>
        assembly.GetTypes()
        |> Array.filter(fun x -> t.IsAssignableFrom(x) && not x.IsInterface && not x.IsAbstract)
        |> Array.map(fun x -> (Activator.CreateInstance(x)) :?> IExample)    
    
    let private PrintExample (example : IExample) =
        let name = example.GetType().Name
        printfn "    %s : %s" name example.Description

    /// List all examples
    let private ListExamples (examples : IExample[]) =
        printfn "Available Examples: "
        examples 
        |> Array.iter (PrintExample)

    /// Find an example by giving the example's name (the type's name)
    let private FindExample (name : string, examples : IExample[]) =
        try
            examples
            |> Array.find (fun x -> x.GetType().Name = name)
            |> Some
        with
        | _ -> None


    let private CommandLineUsage = "
        Command line arguments:\n\
        -list          list examples, if specified, all other arguments are ignored \n\
        -cluster       Prajna cluster file, if not specified, use a local cluster with 2 nodes \n\
        -examples      specify examples to run via a comma seperated string, if not specified, run all examples \n\
        "

    /// Parse the arguments from "-examples"
    let private ParseExamplesArg (arg:string, examples : IExample[]) = 
        if Utils.IsNotNull arg then
            let inputs = arg.Split([|','|], StringSplitOptions.RemoveEmptyEntries)
            let sb = Text.StringBuilder()
            let allValid = ref true
            let results = 
                inputs
                |> Array.map (fun x -> match (FindExample(x, examples)) with
                                       | None ->
                                           allValid := false
                                           sb.Append(sprintf "    '%s' is not found" x).AppendLine() |> ignore
                                           None
                                       | e -> e
                                       
                             )
                |> Array.choose id

            if !allValid then
                (true, String.Empty, results)
            else
                (false, sb.ToString(), null)
        else
            (true, String.Empty, examples)

    /// Run the examples under "namespace" according to arguments 
    let RunExamples (argv : string[], ``namespace`` : string) =
        let args = Array.copy argv
        let parser = ArgumentParser(args)
    
        let shouldList = parser.ParseBoolean("-list", false)
        let clusterArg = parser.ParseString("-cluster", null)
        let examplesArg = parser.ParseString("-examples", null)
    
        let allParsed = parser.AllParsed CommandLineUsage

        if not allParsed then
            parser.PrintUsage CommandLineUsage
        else 
            let examples = GetExamples(Assembly.GetCallingAssembly(), ``namespace``)
            if shouldList then
                ListExamples(examples)
            else
                let ret, msg, examplesToExecute = ParseExamplesArg(examplesArg, examples)
                if not ret then
                    printfn "Error:"
                    printfn "%s" msg
                else
                    Environment.Init()
                    let cluster =  
                        if Utils.IsNotNull clusterArg then
                            Cluster(clusterArg)
                        else 
                            // use local cluster with 2 nodes
                            Cluster("local[2]")
                
                    Prajna.Service.CacheService.Start(cluster)
                    
                    examplesToExecute
                    |> Array.iter (fun e -> 
                           printfn "==============================================================" 
                           printfn "Example '%s': %s" (e.GetType().Name) (e.Description)
                           printfn "--------------------------------------------------------------" 
                           let ret = e.Run(cluster)
                           printfn "--------------------------------------------------------------" 
                           printfn "%s" (if ret then "Succeeded" else "Failed")
                       )

                    Prajna.Service.CacheService.Stop(cluster)                        
                    Environment.Cleanup()