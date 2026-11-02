namespace FS.GG.Algorithms.Decision

// Behaviour trees as a pure tick reducer (ADR-0098 §11 N2). Implemented from the published descriptions:
// M. Colledanchise and P. Ögren, "Behavior Trees in Robotics and AI: An Introduction", CRC Press, 2018,
// doi:10.1201/9780429489105 (memory composites, parallel thresholds); D. Isla, "Handling Complexity in the
// Halo 2 AI", Game Developers Conference, 2005. The interpreter is an explicit stack machine (rule 5).

[<RequireQualifiedAccess>]
type BtStatus =
    | Success
    | Failure
    | Running

[<RequireQualifiedAccess>]
type BehaviourTree<'L> =
    | Leaf of 'L
    | Sequence of BehaviourTree<'L> list
    | Selector of BehaviourTree<'L> list
    | Parallel of successAt: int * failureAt: int * children: BehaviourTree<'L> list
    | Invert of BehaviourTree<'L>
    | ForceSuccess of BehaviourTree<'L>
    | ForceFailure of BehaviourTree<'L>
    | Repeat of times: int * BehaviourTree<'L>
    | Retry of attempts: int * BehaviourTree<'L>

module internal BtKind =
    [<Literal>]
    let Leaf = 0
    [<Literal>]
    let Sequence = 1
    [<Literal>]
    let Selector = 2
    [<Literal>]
    let Parallel = 3
    [<Literal>]
    let Invert = 4
    [<Literal>]
    let ForceSuccess = 5
    [<Literal>]
    let ForceFailure = 6
    [<Literal>]
    let Repeat = 7
    [<Literal>]
    let Retry = 8

[<Sealed; Class>]
type BtProgram<'L>(kind: int[], children: int[][], p1: int[], p2: int[], leafSlot: int[], leaves: 'L[], subtreeEnd: int[]) =
    member _.NodeCount = kind.Length
    member _.Kind = kind
    member _.Children = children
    member _.P1 = p1
    member _.P2 = p2
    member _.LeafSlot = leafSlot
    member _.Leaves = leaves
    member _.SubtreeEnd = subtreeEnd

[<Sealed; Class>]
type BtState(cursors: int[], finished: int[]) =
    member _.CursorArray = cursors
    member _.FinishedArray = finished
    member _.Cursors = List.ofArray cursors
    member _.Finished = List.ofArray finished

    override _.Equals(other: obj) =
        match other with
        | :? BtState as o -> cursors = o.CursorArray && finished = o.FinishedArray
        | _ -> false

    override _.GetHashCode() =
        let mutable h = cursors.Length
        for x in cursors do
            h <- ((h <<< 5) - h) + x
        for x in finished do
            h <- ((h <<< 5) - h) + x
        h

[<RequireQualifiedAccess>]
module BehaviourTree =

    let compile (tree: BehaviourTree<'L>) =
        let kind = ResizeArray<int>()
        let children = ResizeArray<ResizeArray<int>>()
        let p1 = ResizeArray<int>()
        let p2 = ResizeArray<int>()
        let leafSlot = ResizeArray<int>()
        let leaves = ResizeArray<'L>()
        // Preorder by an explicit stack of (subtree, parent index): children are pushed in reverse so the
        // first child, and then its whole subtree, is numbered next.
        let stack = ResizeArray<BehaviourTree<'L> * int>()
        stack.Add((tree, -1))
        while stack.Count > 0 do
            let node, parent = stack.[stack.Count - 1]
            stack.RemoveAt(stack.Count - 1)
            let index = kind.Count
            if parent >= 0 then children.[parent].Add index
            children.Add(ResizeArray<int>())
            let add (k: int) (a: int) (b: int) (kids: BehaviourTree<'L> list) =
                kind.Add k
                p1.Add a
                p2.Add b
                leafSlot.Add -1
                for child in List.rev kids do
                    stack.Add((child, index))
            match node with
            | BehaviourTree.Leaf l ->
                kind.Add BtKind.Leaf
                p1.Add 0
                p2.Add 0
                leafSlot.Add leaves.Count
                leaves.Add l
            | BehaviourTree.Sequence kids -> add BtKind.Sequence 0 0 kids
            | BehaviourTree.Selector kids -> add BtKind.Selector 0 0 kids
            | BehaviourTree.Parallel(successAt, failureAt, kids) ->
                let n = List.length kids
                if n = 0 then invalidArg "tree" "BehaviourTree.compile: a Parallel needs at least one child"
                if successAt < 1 || successAt > n || failureAt < 1 || failureAt > n then
                    invalidArg "tree" "BehaviourTree.compile: Parallel thresholds must be in 1..children"
                add BtKind.Parallel successAt failureAt kids
            | BehaviourTree.Invert c -> add BtKind.Invert 0 0 [ c ]
            | BehaviourTree.ForceSuccess c -> add BtKind.ForceSuccess 0 0 [ c ]
            | BehaviourTree.ForceFailure c -> add BtKind.ForceFailure 0 0 [ c ]
            | BehaviourTree.Repeat(times, c) ->
                if times < 1 then invalidArg "tree" "BehaviourTree.compile: Repeat needs times >= 1"
                add BtKind.Repeat times 0 [ c ]
            | BehaviourTree.Retry(attempts, c) ->
                if attempts < 1 then invalidArg "tree" "BehaviourTree.compile: Retry needs attempts >= 1"
                add BtKind.Retry attempts 0 [ c ]
        let n = kind.Count
        let kids = Array.init n (fun i -> children.[i].ToArray())
        // Preorder subtrees are contiguous: a node's subtree ends where its last child's subtree ends.
        let subtreeEnd = Array.create n 0
        for i in n - 1 .. -1 .. 0 do
            subtreeEnd.[i] <- if kids.[i].Length = 0 then i + 1 else subtreeEnd.[kids.[i].[kids.[i].Length - 1]]
        BtProgram<'L>(kind.ToArray(), kids, p1.ToArray(), p2.ToArray(), leafSlot.ToArray(), leaves.ToArray(), subtreeEnd)

    let initial (program: BtProgram<'L>) =
        BtState(Array.create program.NodeCount 0, Array.create program.NodeCount 0)

    let private run
        (program: BtProgram<'L>)
        (leaf: 'L -> 'B -> BtStatus * 'B)
        (state: BtState)
        (blackboard: 'B)
        (trace: ResizeArray<int>)
        =
        let kind = program.Kind
        let children = program.Children
        let cur = Array.copy state.CursorArray
        let fin = Array.copy state.FinishedArray
        if cur.Length <> program.NodeCount then
            invalidArg "state" "BehaviourTree.tick: the state belongs to a different program"
        let mutable bb = blackboard
        let frames = ResizeArray<int>()
        let awaiting = ResizeArray<bool>()
        let position = ResizeArray<int>()
        let mutable ret = BtStatus.Success
        let push (node: int) =
            frames.Add node
            awaiting.Add false
            position.Add 0
        let descend (top: int) (child: int) =
            awaiting.[top] <- true
            push child
        // Clears a whole subtree: memory, and the parallel results of every node below its root.
        let resetSubtree (root: int) =
            for k in root .. program.SubtreeEnd.[root] - 1 do
                cur.[k] <- 0
                if k > root then fin.[k] <- 0
        // Finish the top frame, keeping the invariant "a finished node's whole subtree is idle". A node
        // finishes only after every descendant it ticked has finished, and a finished node is already idle, so
        // clearing the node's own cursor suffices, except for a Parallel, which halts its unfinished (still
        // running) children by clearing their subtrees. Cost: a tick is O(nodes it visits) plus O(size of the
        // subtrees a finishing Parallel halts). That is not quadratic, but it is not bounded by the visited
        // nodes alone. A full subtree clear on every finish would cost O(depth^2) on a deep tree. The
        // reference interpreter in the tests clears whole subtrees and must agree.
        let finish (status: BtStatus) =
            let top = frames.Count - 1
            let node = frames.[top]
            if status <> BtStatus.Running then
                cur.[node] <- 0
                if kind.[node] = BtKind.Parallel then
                    for c in children.[node] do
                        if fin.[c] = 0 then resetSubtree c
                        fin.[c] <- 0
            frames.RemoveAt top
            awaiting.RemoveAt top
            position.RemoveAt top
            ret <- status
        push 0
        while frames.Count > 0 do
            let top = frames.Count - 1
            let node = frames.[top]
            let waiting = awaiting.[top]
            awaiting.[top] <- false
            let kids = children.[node]
            match kind.[node] with
            | BtKind.Leaf ->
                let status, next = leaf program.Leaves.[program.LeafSlot.[node]] bb
                bb <- next
                trace.Add node
                finish status
            | BtKind.Sequence
            | BtKind.Selector ->
                // Sequence continues on Success and stops on Failure; Selector is the mirror image.
                let proceed = if kind.[node] = BtKind.Sequence then BtStatus.Success else BtStatus.Failure
                let stop = if kind.[node] = BtKind.Sequence then BtStatus.Failure else BtStatus.Success
                if waiting && ret = BtStatus.Running then
                    finish BtStatus.Running
                elif waiting && ret = stop then
                    finish stop
                else
                    if waiting then cur.[node] <- cur.[node] + 1
                    if cur.[node] >= kids.Length then finish proceed else descend top kids.[cur.[node]]
            | BtKind.Parallel ->
                if waiting then
                    let c = kids.[position.[top] - 1]
                    if ret = BtStatus.Success then fin.[c] <- 1
                    elif ret = BtStatus.Failure then fin.[c] <- 2
                let mutable j = position.[top]
                while j < kids.Length && fin.[kids.[j]] <> 0 do
                    j <- j + 1
                if j < kids.Length then
                    position.[top] <- j + 1
                    descend top kids.[j]
                else
                    let mutable succeeded = 0
                    let mutable failed = 0
                    for c in kids do
                        if fin.[c] = 1 then succeeded <- succeeded + 1
                        elif fin.[c] = 2 then failed <- failed + 1
                    if succeeded >= program.P1.[node] then finish BtStatus.Success
                    elif failed >= program.P2.[node] then finish BtStatus.Failure
                    elif succeeded + failed = kids.Length then finish BtStatus.Failure
                    else finish BtStatus.Running
            | BtKind.Invert
            | BtKind.ForceSuccess
            | BtKind.ForceFailure ->
                if not waiting then
                    descend top kids.[0]
                elif ret = BtStatus.Running then
                    finish BtStatus.Running
                else
                    match kind.[node] with
                    | BtKind.Invert -> finish (if ret = BtStatus.Success then BtStatus.Failure else BtStatus.Success)
                    | BtKind.ForceSuccess -> finish BtStatus.Success
                    | _ -> finish BtStatus.Failure
            | _ ->
                // Repeat counts successes up to P1 (a failure ends it); Retry counts failures up to P1 (a
                // success ends it). Both re-tick the child within this tick.
                let counted = if kind.[node] = BtKind.Repeat then BtStatus.Success else BtStatus.Failure
                if not waiting then
                    descend top kids.[0]
                elif ret = BtStatus.Running then
                    finish BtStatus.Running
                elif ret = counted then
                    cur.[node] <- cur.[node] + 1
                    if cur.[node] >= program.P1.[node] then finish counted else descend top kids.[0]
                else
                    finish ret
        ret, BtState(cur, fin), bb

    let tick (program: BtProgram<'L>) (leaf: 'L -> 'B -> BtStatus * 'B) (state: BtState) (blackboard: 'B) =
        run program leaf state blackboard (ResizeArray())

    let tickTraced (program: BtProgram<'L>) (leaf: 'L -> 'B -> BtStatus * 'B) (state: BtState) (blackboard: 'B) =
        let trace = ResizeArray()
        let status, next, bb = run program leaf state blackboard trace
        status, next, bb, List.ofSeq trace
