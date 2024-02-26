﻿module SheetBeautifyHelpers

open CommonTypes
open DrawModelType
open DrawModelType.SymbolT
open DrawModelType.BusWireT
open Optics
open Optics.Operators
open BlockHelpers
open Helpers
open BusWire


//-----------------------------------------------------------------------------------------------
// visibleSegments is included here as ahelper for info, and because it is needed in project work
//-----------------------------------------------------------------------------------------------

/// The visible segments of a wire, as a list of vectors, from source end to target end.
/// Note that in a wire with n segments a zero length (invisible) segment at any index [1..n-2] is allowed 
/// which if present causes the two segments on either side of it to coalesce into a single visible segment.
/// A wire can have any number of visible segments - even 1.
let visibleSegments (wId: ConnectionId) (model: SheetT.Model): XYPos list =

    let wire = model.Wire.Wires[wId] // get wire from model

    /// helper to match even and off integers in patterns (active pattern)
    let (|IsEven|IsOdd|) (n: int) = match n % 2 with | 0 -> IsEven | _ -> IsOdd

    /// Convert seg into its XY Vector (from start to end of segment).
    /// index must be the index of seg in its containing wire.
    let getSegmentVector (index:int) (seg: BusWireT.Segment) =
        // The implicit horizontal or vertical direction  of a segment is determined by 
        // its index in the list of wire segments and the wire initial direction
        match index, wire.InitialOrientation with
        | IsEven, BusWireT.Vertical | IsOdd, BusWireT.Horizontal -> {X=0.; Y=seg.Length}
        | IsEven, BusWireT.Horizontal | IsOdd, BusWireT.Vertical -> {X=seg.Length; Y=0.}

    /// Return a list of segment vectors with 3 vectors coalesced into one visible equivalent
    /// if this is possible, otherwise return segVecs unchanged.
    /// Index must be in range 1..segVecs
    let tryCoalesceAboutIndex (segVecs: XYPos list) (index: int)  =
        if segVecs[index] =~ XYPos.zero
        then
            segVecs[0..index-2] @
            [segVecs[index-1] + segVecs[index+1]] @
            segVecs[index+2..segVecs.Length - 1]
        else
            segVecs

    wire.Segments
    |> List.mapi getSegmentVector
    |> (fun segVecs ->
            (segVecs,[1..segVecs.Length-2])
            ||> List.fold tryCoalesceAboutIndex)

// given a list return all unique pairings of its' elements ingnoring ordering
let allDistinctPairs (lst : 'a list) = 
    ((1,[]),lst)
    ||> List.fold (fun (i,lstAcc) v ->  
        let updatedList = 
            lst[i ..]
            |> List.allPairs [v]
            |> List.append lstAcc
        (i+1, updatedList)
    ) 
    |> snd


// had to define another one because predefined one is missing one of the coordinates
/// Get the coordinate fixed in an ASegment. NB - ASegments can't be zero length
let inline getFixedCoord (aSeg: ASegment) =
    match aSeg.Orientation with 
    | Vertical -> (aSeg.Start.X, aSeg.End.X) 
    | Horizontal -> (aSeg.Start.Y, aSeg.End.Y)


//-----------------Module for beautify Helper functions--------------------------//
// Typical candidates: all individual code library functions.
// Other helpers identified by Team

// Function 1 : The dimensions of a custom component symbol
let getCustomComponentDimensionB1R (customComponent : SymbolT.Symbol) : {|H:float;W:float|} =
    let comp = Optic.get SymbolT.component_ customComponent

    {|H=comp.H;W=comp.W|}

let setCustomComponentDimensionB1W (newDims : {|H:float;W:float|}) (customComponent : SymbolT.Symbol) : SymbolT.Symbol =
    let comp = Optic.get SymbolT.component_ customComponent

    customComponent 
    |> Optic.set SymbolT.component_ {comp with H = newDims.H; W = newDims.W}

let componentDimension_B1RW = Lens.create getCustomComponentDimensionB1R setCustomComponentDimensionB1W


// Function 2 : The position of a symbol on a sheet
let setSymbolPositionB2W (symId : ComponentId) (sheet : SheetT.Model) (newPosition : XYPos) = 
    let positionLens = (SheetT.symbolOf_ symId) >-> SymbolT.posOfSym_
    Optic.set positionLens newPosition sheet

// Function 3 : Read/write the order of ports on a specified side of a symbol
let orderOfPortsBySide_B3RW (side : Edge) =  
    let orderOfEdge_ = 
        ((fun (orderMap : Map<Edge, string list>) -> Map.find side orderMap),
        (fun (newOrder : string list) orderMap -> Map.add side newOrder orderMap))
        ||> Lens.create  
    
    SymbolT.portMaps_
    >-> SymbolT.order_
    >-> orderOfEdge_

// Function 4 : The reversed state of the inputs of a MUX2
let reversedState_B4RW = 
    Lens.create (fun a -> a.ReversedInputPorts) (fun s a -> {a with ReversedInputPorts = s})

// Function 5 : The position of a port on the sheet. It cannot directly be written.
let getPortPosInSheetB5R ( portId : string ) ( sheet : SheetT.Model ) = 
    Symbol.getPortLocation None ( sheet ^. SheetT.symbol_ ) portId

// Function 6 : The Bounding box of a symbol outline (position is contained in this)
let getBoundingBoxOfSymbolOutlineB6R = Symbol.getSymbolBoundingBox

let stransform_ = Lens.create (fun a -> a.STransform) (fun s a -> {a with STransform = s})
let rotation_ = Lens.create (fun a -> a.Rotation) (fun s a -> {a with Rotation = s})
let flip_ = Lens.create (fun a -> a.Flipped) (fun s a -> {a with Flipped = s})



// Function 7 : The rotation state of a symbol
let rotationOfSymbol_B7RW = stransform_ >-> rotation_

// Function 8 : The flip state of a symbol
let flipOfSymbol_B8RW = stransform_ >-> flip_

// Function 9 : The number of pairs of symbols that intersect each other. See Tick3 for a related function. Count over all pairs of symbols
let countSymbolIntersectPairsT1R ( sheet : SheetT.Model ) = 
    let boxes = 
        mapValues sheet.BoundingBoxes
        |> Array.toList
        |> List.mapi (fun n box -> n,box)

    allDistinctPairs boxes
    |> List.filter (fun ((n1, box1),(n2,box2)) -> (n1 <> n2) && BlockHelpers.overlap2DBox box1 box2)
    |> List.length


// taken from findWireSymbolIntersection
let allSymbolBBoxInSheet ( sheet : SheetT.Model) =
    sheet.Wire.Symbol.Symbols
    |> Map.values
    |> Seq.toList
    |> List.filter (fun s -> s.Annotation = None)
    |> List.map (fun s -> (s.Component.Type, Symbol.getSymbolBoundingBox s))

// Function 10 : The number of distinct wire visible segments that intersect with one or more symbols. See Tick3.HLPTick3.visibleSegments for a helper. Count over all visible wire segments.
let countDistinctWireSegmentIntersectSymbolT2R ( sheet : SheetT.Model ) = 
    allSymbolBBoxInSheet sheet
    |> List.collect (fun (_compType, bbox) -> 
        getWiresInBox bbox sheet.Wire 
        |> List.map (fun (wire, segI) -> wire.Segments[segI])
    ) 
    |> List.length


// function 10 : The number of distinct pairs of segments that cross each other at right angles. 
// Does not include 0 length segments or segments on same net intersecting at one end, or segments on same net on top of each other. Count over whole sheet.
let countDistinctWireSegmentOrthogonalIntersectT3R ( sheet : SheetT.Model) = 
    // get a list of segments which intersect at right angles
    // for each segment obtain asbolute start and end position and corresponding wire Id and segment 
    // check orthogonality by comparing each segment with all other segments with opposite orientation and withing the range of that segment

    let allSegments = 
        sheet.Wire.Wires
        |> Map.toList
        |> List.collect (fun (wId, wire) -> getAbsSegments wire)
        |> List.distinct
        |> List.mapi (fun i seg -> (i, seg))

    allSegments
    |> List.collect (fun (i1, seg1) ->
        allSegments
        |> List.filter (fun (i2, seg2) ->
            i1 <> i2 
            &&
            seg2.Orientation <> seg1.Orientation 
            &&
            match seg2.Orientation with
            | Vertical -> 
                (seg2.Start.X < seg1.End.X) 
                && (seg2.Start.X > seg1.Start.X) 
                && (seg2.Start.Y < seg1.Start.Y)
            | Horizontal -> 
                (seg2.Start.Y < seg1.End.Y) 
                && (seg2.Start.Y > seg1.Start.Y) 
                && (seg2.Start.X < seg1.Start.X)
        ) 
        |> List.map (fun (i2, seg2) -> if i2 < i1 then (i1, i2) else (i2, i1))
    )
    |> List.distinct
    |> List.length



// function 11 : Sum of wiring segment length, counting only one when there are N same-net
// segments overlapping (this is the visible wire length on the sheet). Count over whole sheet
let wiringSegmentLengthT4R (sheet : SheetT.Model) = 
    // calculate overall length and remove length of overlapping segments
    // for each unique pair of segments keep the ones that overlap
    // for a given segment find out how many overlapping pairs there are
    // remove n-1 times the length of the segment
    let segments = 
        sheet.Wire.Wires
        |> Map.toList
        |> List.collect (fun (wid, wire) -> getAbsSegments wire)
        |> List.mapi (fun i s -> (i,s))

    let overlappingLength = 

        ((0, Map.empty),segments)
        ||> List.fold (fun (i, overlappedMap ) (currId, currSeg) -> 
                
            let isSegmentOverlapped segId = 
                let overlappedSegments = 
                    Map.toList overlappedMap 
                    |> List.unzip
                    |> fst

                List.tryFind (fun index -> index = segId) overlappedSegments
                |> function | Some _ -> true | None -> false
            
            if not <| isSegmentOverlapped i
            then
                let unexploredSegments = 
                    segments[i..]
                    |> List.filter (fun (segId, seg) -> not <| isSegmentOverlapped segId)
                
                let newOverlaps = 
                    unexploredSegments
                    |> List.collect (fun (segId2, comparisonSeg) ->
                        match 
                            currSeg.Orientation = comparisonSeg.Orientation 
                            && overlap1D (getFixedCoord currSeg) (getFixedCoord comparisonSeg)
                        with
                        | true -> [(segId2, currSeg.Segment.Length)] // this does not take into account segments of different lengths overlapping
                        | false -> []
                    )

                (i+1, 
                (overlappedMap, newOverlaps)
                ||> List.fold (fun returnMap (segId, overlapLength) ->
                    Map.add segId (currId, overlapLength) returnMap 
                ))
            else 
                (i+1, overlappedMap)
        )
        |> snd 
        |> Map.toList
        |> List.unzip |> snd // list of segIds and their length with an occurence for each time they overlap
        |> (fun uniqueOverlaps ->
            let counts = List.countBy (fun (segId, _len) -> segId) uniqueOverlaps
            let distinct = List.distinctBy (fun (segId, _len) -> segId) uniqueOverlaps

            List.zip counts distinct
            |> List.map (fun ((sid, count),(sid,len)) -> (sid, count, len))
        )
        |> List.fold (fun lenAcc (sid,count,len) ->
            match count with
            | c when c >= 1 -> lenAcc + len * (float c)
            | _ -> failwithf "Should not reach this, overlapping segments should have at least 1 overlaps"
        ) 0.0

    let totalLength = 
        sheet.Wire.Wires
        |> Map.toList
        |> List.map (fun (wid, wire) -> getWireLength wire)
        |> List.reduce (+)
    
    totalLength - overlappingLength

// function 12 : Number of visible wire right-angles. Count over whole sheet.
let countVisibleRightAnglesT5R ( sheet : SheetT.Model) =
    sheet.Wire.Wires
    |> Map.keys
    |> Seq.toList
    |> List.map (fun wid -> 
        visibleSegments wid sheet 
        |> List.length 
        |> (fun res -> res / 2))
        // there as many right angles as half the number of visible segments
    |> List.reduce (+)

// function 13 : 
// The zero-length segments in a wire with non-zero segments on either side that have 
// Lengths of opposite signs lead to a wire retracing itself. Note that this can also apply
// at the end of a wire (where the zero-length segment is one from the end). This is a
// wiring artifact that should never happen but errors in routing or separation can
// cause it. Count over the whole sheet. Return from one function a list of all the
// segments that retrace, and also a list of all the end of wire segments that retrace so
// far that the next segment (index = 3 or Segments.Length – 4) - starts inside a symbol.
let getRetraceSegmentsOfWire ( wire : BusWireT.Wire ) = 
    (([], wire.Segments), wire.Segments)
    ||> List.fold (fun (retraceL, remainingSegs) seg ->
        match remainingSegs with
        | prev::curr::next::tail -> 
            let isRetrace = curr.Length = 0 && (sign prev.Length <> sign next.Length)
            let newRetraceL = if isRetrace then retraceL @ [(prev,curr,next)] else retraceL
            (newRetraceL, [curr;next] @ tail)
        | _ -> (retraceL, [])
    )
    |> fst
    |> function
    | [] -> None
    | s -> Some s

let getEndOfWireRetrace (wire : BusWireT.Wire) (model : BusWireT.Model) retraceSegmentList = 
    let getNewStartPos segIndex posChange = 
        let oldSeg = getASegmentFromId model (segIndex,wire.WId)
        match oldSeg.Orientation with
        | Horizontal -> {oldSeg.Start with X = oldSeg.Start.X + posChange }
        | Vertical -> {oldSeg.Start with Y = oldSeg.Start.Y + posChange }

    let startWire = 
        List.tryHead retraceSegmentList 
        |> Option.bind (fun (start : Segment,_zero,next) ->
            if start.Index = 0 
            then Some [start, getNewStartPos start.Index next.Length] 
            else None
        ) |> Option.defaultValue []

    let endWire = 
        List.tryLast retraceSegmentList 
        |> Option.bind (fun (prev ,_zero,endSeg : Segment) ->
            if endSeg.Index = wire.Segments.Length - 1 
            then Some [endSeg, getNewStartPos endSeg.Index prev.Length] 
            else None
        ) |> Option.defaultValue []
    
    startWire @ endWire

let startInsideSymbol (sheet : SheetT.Model) startPos = 
    sheet
    |> Optic.get SheetT.boundingBoxes_
    |> Map.toList
    |> List.map (fun (_cid, bbox) ->
        overlap2DBox bbox {TopLeft=startPos;W=0;H=0}
    ) |> List.reduce (||)

let getRetraceSegmentsT6R ( sheet : SheetT.Model ) =
    sheet.Wire.Wires
    |> Map.toList
    |> List.fold (fun (retL,endOfWireL) (_wId, wire) -> 
        let retracedSegments = getRetraceSegmentsOfWire wire

        let startInsideSymbol =
            retracedSegments
            |> Option.map (fun s -> 
                s
                |> getEndOfWireRetrace wire sheet.Wire 
                |> List.collect (fun (endOfWireS, retractedStartPos) -> if startInsideSymbol sheet retractedStartPos then [endOfWireS] else [])
            )
            |> Option.defaultValue []

        (retL @ Option.defaultValue [] retracedSegments,endOfWireL @ startInsideSymbol)
    ) ([],[])