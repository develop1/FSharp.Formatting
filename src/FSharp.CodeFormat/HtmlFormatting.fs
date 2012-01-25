﻿// --------------------------------------------------------------------------------------
// F# CodeFormat (HtmlFormatting.fs)
// (c) Tomas Petricek, 2012, Available under Apache 2.0 license.
// --------------------------------------------------------------------------------------

module FSharp.CodeFormat.Html

open System
open System.IO
open System.Web
open System.Text
open System.Collections.Generic
open FSharp.CodeFormat

// --------------------------------------------------------------------------------------
// Context used by the formatter
// --------------------------------------------------------------------------------------

/// Mutable type that formats tool tips and keeps the generated HTML
type ToolTipFormatter(prefix) = 
  let tips = new Dictionary<ToolTipSpans, int * string>()
  let mutable count = 0
  let mutable uniqueId = 0

  /// Formats tip and returns assignments for 'onmouseover' and 'onmouseout'
  member x.FormatTip (tip:ToolTipSpans) overlapping formatFunction = 
    uniqueId <- uniqueId + 1
    let stringIndex =
      match tips.TryGetValue(tip) with
      | true, (idx, _) -> idx
      | _ -> 
        count <- count + 1
        tips.Add(tip, (count, formatFunction tip))
        count
    // stringIndex is the index of the tool tip
    // uniqueId is globally unique id of the occurrence
    if overlapping then
      // The <span> may contain other <span>, so we need to 
      // get the element and check where the mouse goes...
      String.Format
        ( "id=\"{0}t{1}\" onmouseout=\"hideTip(event, '{0}{1}', {2})\" " + 
          "onmouseover=\"showTip(event, '{0}{1}', {2}, document.getElementById('{0}t{1}'))\" ",
          prefix, stringIndex, uniqueId )
    else
      String.Format
        ( "onmouseout=\"hideTip(event, '{0}{1}', {2})\" " + 
          "onmouseover=\"showTip(event, '{0}{1}', {2})\" ",
          prefix, stringIndex, uniqueId )
  
  /// Returns all generated tool tip elements
  member x.WriteTipElements (writer:TextWriter) = 
    for (KeyValue(_, (index, html))) in tips do
      writer.Write(sprintf "<div class=\"tip\" id=\"%s%d\">%s</div>" prefix index html)

/// Represents context used by the formatter
type FormattingContext = 
  { AddLines : bool
    GenerateErrors : bool
    Writer : TextWriter 
    FormatTip : ToolTipSpans -> bool -> (ToolTipSpans -> string) -> string }

// --------------------------------------------------------------------------------------
// Formats various types from 'SourceCode.fs' as HTML
// --------------------------------------------------------------------------------------

/// Formats tool tip information and returns a string
let formatToolTipSpans spans = 
  let sb = StringBuilder()
  use wr = new StringWriter(sb)
  // Inner recursive function that does the formatting
  let rec format spans = spans |> List.iter (function
    | Emphasis(spans) ->
        wr.Write("<em>")
        format spans
        wr.Write("</em>")        
    | Literal(string) ->
        let spaces = string.Length - string.TrimStart(' ').Length
        wr.Write(String.replicate spaces "&160;")
        wr.Write(string.Substring(spaces))
    | HardLineBreak ->
        wr.Write("<br />"))
  format spans
  sb.ToString()

/// Format token spans such as tokens, omitted code etc.
let rec formatTokenSpans (ctx:FormattingContext) = List.iter (function
  | Error(kind, message, body) when ctx.GenerateErrors ->
      let tipAttributes = ctx.FormatTip [Literal message] true formatToolTipSpans
      ctx.Writer.Write("<span ")
      ctx.Writer.Write(tipAttributes)
      ctx.Writer.Write("class=\"cerr\">")
      formatTokenSpans { ctx with FormatTip = fun _ _ _ -> "" } body
      ctx.Writer.Write("</span>")
  
  | Error(_, _, body) ->
      formatTokenSpans ctx body

  | Output(body) ->
      ctx.Writer.Write("<span class=\"fsi\">")
      ctx.Writer.Write(body)
      ctx.Writer.Write("</span>")

  | Omitted(body, hidden) ->
      let tipAttributes = ctx.FormatTip [Literal body] false formatToolTipSpans
      ctx.Writer.Write("<span ")
      ctx.Writer.Write(tipAttributes)
      ctx.Writer.Write("class=\"omitted\">")
      ctx.Writer.Write(body)
      ctx.Writer.Write("</span>")
      
  | Token(kind, body, tip) ->
      // Generate additional attributes for ToolTip
      let tipAttributes = 
        match tip with
        | Some(tip) -> ctx.FormatTip tip false formatToolTipSpans
        | _ -> ""

      // Get CSS class name of the token
      let color = 
        match kind with
        | TokenKind.Comment -> "c"
        | TokenKind.Default -> ""
        | TokenKind.Identifier -> "i"
        | TokenKind.Inactive -> "inactive"
        | TokenKind.Keyword -> "k"
        | TokenKind.Number -> "n"
        | TokenKind.Operator -> "o"
        | TokenKind.Preprocessor -> "prep"
        | TokenKind.String -> "s"

      if kind <> TokenKind.Default then
        // Colorize token & add tool tip
        ctx.Writer.Write("<span ")
        ctx.Writer.Write(tipAttributes)
        ctx.Writer.Write("class=\"" + color + "\">")
        ctx.Writer.Write(HttpUtility.HtmlEncode(body))
        ctx.Writer.Write("</span>")
      else       
        ctx.Writer.Write(HttpUtility.HtmlEncode(body)) )

/// Generate HTML with the specified snippets
let formatSnippets (ctx:FormattingContext) (snippets:Snippet[]) =
 [| for (Snippet(title, lines)) in snippets do
      // Generate snippet to a local StringBuilder
      let mainStr = StringBuilder()
      let ctx = { ctx with Writer = new StringWriter(mainStr) }

      // Generate <pre> tag for the snippet
      ctx.Writer.WriteLine("<pre class=\"fssnip\">")
      let numberLength = lines.Length.ToString().Length
      let linesLength = lines.Length
      // Print all lines of the snippet
      lines |> List.iteri (fun index (Line spans) ->
        let isLast = index = linesLength - 1
        // Add line number to the beginning
        if ctx.AddLines then
          let lineStr = (index + 1).ToString().PadLeft(numberLength)
          ctx.Writer.Write("<span class=\"l\">{0}: </span>", lineStr)

        // Write tokens & end of the line
        formatTokenSpans ctx spans
        if not isLast then ctx.Writer.WriteLine() )

      // Close the <pre> tag for this snippet          
      ctx.Writer.WriteLine("</pre>")
      ctx.Writer.Close() 
      yield title, mainStr.ToString() |]

/// Format snippets and return HTML for <pre> tags together
/// wtih HTML for ToolTips (to be added to the end of document)
let format addLines addErrors prefix (snippets:Snippet[]) = 
  let tipf = ToolTipFormatter(prefix)
  let ctx =  { AddLines = addLines; GenerateErrors = addErrors
               Writer = null; FormatTip = tipf.FormatTip }
  
  // Generate main HTML for snippets
  let snippets = formatSnippets ctx snippets
  // Generate HTML with ToolTip tags
  let tipStr = StringBuilder()
  tipf.WriteTipElements(new StringWriter(tipStr))
  snippets, tipStr.ToString()