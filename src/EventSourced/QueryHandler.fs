﻿namespace InformedICU.EventSourced

module QueryHandler =

    let rec private choice (queryHandler : QueryHandler<_> list) query =
        async {
            match queryHandler with
            | handler :: rest ->
                match! handler query with
                | NotHandled ->
                    return! choice rest query

                | Handled response ->
                    return Handled response

                | QueryError response ->
                    return QueryError response

            | _ -> return NotHandled
        }

    let initialize queryHandlers : QueryHandler<_> =
        choice queryHandlers
        


