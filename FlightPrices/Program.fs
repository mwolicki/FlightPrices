#if INTERACTIVE
#r @"../packages/FSharp.Data.2.3.0/lib/net40/FSharp.Data.dll"
#load "ApiKey.fs"
#endif
open FlightPrices
open FSharp.Data
open System

type Places = JsonProvider<"places.json">
type Flights = JsonProvider<"flights.json">
type FlightData = {Departure:DateTime; Arrival:DateTime; Carriers : string list }
                  override d.ToString() = sprintf "%O - %s [%s]" d.Departure (d.Arrival.ToShortTimeString()) (d.Carriers |> String.concat ", ")
type Flight = { Outbound:FlightData
                Inbound:FlightData
                Price:decimal}


let url = @"http://partners.api.skyscanner.net/apiservices/pricing/v1.0"


let getAirport q =
    @"http://partners.api.skyscanner.net/apiservices/autosuggest/v1.0/GB/GBP/en-GB?query=" + q + @"&apiKey=" + apiKey
    |> Places.AsyncLoad 

let getFlights originplace destinationplace (outbounddate:DateTime) (inbounddate:DateTime) = async {
    let param = 
        ["apiKey", apiKey
         "country", "UK"
         "currency", "GBP"
         "locale", "en-GB"
         "originplace", originplace
         "destinationplace", destinationplace
         "outbounddate", outbounddate.ToString("yyyy-MM-dd")
         "inbounddate", inbounddate.ToString("yyyy-MM-dd")
         "adults","1"
         "locationschema", "iata"
         ]
    let! resp = Http.AsyncRequestStream(url, silentHttpErrors = false, body = FormValues param)

    let! flights = resp.Headers.["Location"] + "?apiKey=" + apiKey 
                    + "&outbounddepartstarttime=" + outbounddate.ToString("HH:mm") 
                    + "&inbounddepartstarttime=" + inbounddate.ToString("HH:mm") 
                    |> Flights.AsyncLoad 
    let legs = flights.Legs |> Array.map(fun x->x.Id, x) |> Map.ofArray
    let carriers = flights.Carriers |> Array.map(fun x->x.Id, x) |> Map.ofArray
    let getCarrierName id = carriers.[id].Name

    return flights.Itineraries 
            |> Array.map(fun y->
                let inbound = legs.[y.InboundLegId]
                let outbound = legs.[y.OutboundLegId]

                { Inbound = { Departure = inbound.Departure; Arrival = inbound.Arrival; Carriers = inbound.Carriers |> Array.map getCarrierName |> List.ofArray }
                  Outbound = { Departure = outbound.Departure; Arrival = outbound.Arrival; Carriers = outbound.Carriers |> Array.map getCarrierName |> List.ofArray }
                  Price = y.PricingOptions |> Array.map(fun p->p.Price) |> Array.min
                }) |> List.ofArray}


let res = 
    [ for i = 0 to 15 do
        yield DateTime(2016,5,6, 20,0,0).AddDays(i * 7 |> float), DateTime(2016,5,9).AddDays(i * 7 |> float)
        yield DateTime(2016,5,5, 20,0,0).AddDays(i * 7 |> float), DateTime(2016,5,9).AddDays(i * 7 |> float)]
    |> List.map (fun (a,b) -> getFlights "LOND-sky" "POZ" a b)
    |> Async.Parallel
    |> Async.RunSynchronously
    |> Seq.collect id
    |> List.ofSeq

res |> List.minBy (fun x->x.Price) |> printfn "%A"



res |> List.sortBy (fun x->x.Price) |> List.iter (fun x-> printfn "%M GBP\t %O <--> %O" x.Price x.Outbound x.Inbound)
