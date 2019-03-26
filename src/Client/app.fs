module App

open Elmish

open Fable
open Fable.FontAwesome
open Fable.Core.JsInterop
open Fable.Helpers.React
open Fable.Helpers.React.Props
open Fable.PowerPack
open Fable.Recharts
open Fable.Recharts.Props

open Fulma

open Shared

/// The different elements of the completed report.
type Report =
    { Location : LocationResponse
      Crimes : CrimeResponse array
      Weather : WeatherResponse }

type ServerState = Idle | Loading | ServerError of string

/// The overall data model driving the view.
type Model =
    { Postcode : string
      ValidationError : string option
      ServerState : ServerState
      Report : Report option }

/// The different types of messages in the system.
type Msg =
    | GetReport
    | PostcodeChanged of string
    | GotReport of Report
    | ErrorMsg of exn
    | Clear

/// The init function is called to start the message pump with an initial view.
let init () = 
    { Postcode = ""
      Report = None
      ValidationError = None
      ServerState = Idle }, Cmd.ofMsg (PostcodeChanged "")

let decoderForLocationResponse = Thoth.Json.Decode.Auto.generateDecoder<LocationResponse> ()
let decoderForCrimeResponse = Thoth.Json.Decode.Auto.generateDecoder<CrimeResponse array>()
let decoderForWeatherResponse = Thoth.Json.Decode.Auto.generateDecoder<WeatherResponse> ()

let inline getJson<'T> (response:Fetch.Fetch_types.Response) = response.text() |> Promise.map Thoth.Json.Decode.Auto.unsafeFromString<'T>

let getResponse postcode = promise {
    let! location = Fetch.fetchAs<LocationResponse> (sprintf "/api/distance/%s" postcode) decoderForLocationResponse []
    let! crimes = Fetch.tryFetchAs (sprintf "api/crime/%s" postcode) decoderForCrimeResponse [] |> Promise.map (Result.defaultValue [||])
    // let! weather = Fetch.fetchAs<WeatherResponse> (sprintf "/api/weather/%s" postcode) decoderForWeatherResponse []

    let postcodeRequest = {Postcode = postcode}

    // let! locationResponse = Fetch.postRecord (sprintf "/api/distance") postcodeRequest []
    // let! location = getJson<LocationResponse> locationResponse

    // let! crimesResponse = Fetch.postRecord (sprintf "/api/crime") postcodeRequest []
    // let! crimes = getJson<CrimeResponse array> crimesResponse

    let! weatherResponse = Fetch.postRecord (sprintf "/api/weather") postcodeRequest []
    let! weather = getJson<WeatherResponse> weatherResponse

    return { Location = location; Crimes = crimes; Weather = weather } }
 
/// The update function knows how to update the model given a message.
let update msg model =
    match model, msg with
    | { ValidationError = None; Postcode = postcode }, GetReport ->
        { model with ServerState = Loading }, Cmd.ofPromise getResponse postcode GotReport ErrorMsg
    | _, GetReport -> model, Cmd.none
    | _, GotReport response ->
        { model with
            ValidationError = None
            Report = Some response
            ServerState = Idle }, Cmd.none
    | _, PostcodeChanged p ->
        { model with
            Postcode = p
            ValidationError = if Validation.validatePostcode p then None else Some("Invalid Postal Code!")}, Cmd.none
    | _, ErrorMsg e -> { model with ServerState = ServerError e.Message }, Cmd.none
    | _, Clear -> init()

[<AutoOpen>]
module ViewParts =
    let basicTile title options content =
        Tile.tile options [
            Notification.notification [ Notification.Props [ Style [ Height "100%"; Width "100%" ] ] ]
                (Heading.h2 [] [ str title ] :: content)
        ]
    let childTile title content =
        Tile.child [ ] [
            Notification.notification [ Notification.Props [ Style [ Height "100%"; Width "100%" ] ] ]
                (Heading.h2 [ ] [ str title ] :: content)
        ]
    
    let crimeTile crimes =
        let cleanData = crimes |> Array.map (fun c -> { c with Crime = c.Crime.[0..0].ToUpper() + c.Crime.[1..].Replace('-', ' ') } )
        basicTile "Crime" [ ] [
            barChart
                [ Chart.Data cleanData
                  Chart.Width 600.
                  Chart.Height 500.
                  Chart.Layout Vertical ]
                [ xaxis [ Cartesian.Type "number" ] []
                  yaxis [ Cartesian.Type "category"; Cartesian.DataKey "Crime"; Cartesian.Width 200. ] []
                  bar [ Cartesian.DataKey "Incidents" ] [] ]
        ]

    let getBingMapUrl latLong =
        sprintf "https://www.bing.com/maps/embed?h=400&w=800&cp=%f~%f&lvl=11&typ=s&FORM=MBEDV8" latLong.Latitude latLong.Longitude

    let bingMapTile (latLong:LatLong) =
        basicTile "Map" [ Tile.Size Tile.Is12 ] [
            iframe [
                Style [ Height 410; Width 810 ]
                Src (getBingMapUrl latLong)
            ] [ ]
        ]

    let weatherTile weatherReport =
        let formatTemp() = 
            (sprintf "%.1f\u00b0C" weatherReport.AverageTemperature)

        childTile "Weather" [
            Level.level [ ] [
                Level.item [ Level.Item.HasTextCentered ] [
                    div [ ] [
                        Level.heading [ ] [
                            Image.image [ Image.Is128x128 ] [
                                img [ Src(sprintf "https://www.metaweather.com/static/img/weather/%s.svg" weatherReport.WeatherType.Abbreviation) ]
                            ]
                        ]
                        Level.title [ ] [
                            Heading.h3 [ Heading.Is4; Heading.Props [ Style [ Width "100%" ] ] ] [
                                str (formatTemp())
                            ]
                        ]
                    ]
                ]
            ]
        ]
    let locationTile (location:LocationResponse) =
        childTile "Location" [
            div [ ] [
                Heading.h3 [ ] [ str location.Location.Town ]
                Heading.h4 [ ] [ str location.Location.Region ]
                Heading.h4 [ ] [ sprintf "%.1fKM to London" location.DistanceToLondon |> str ]
            ]
        ]
             

/// The view function knows how to render the UI given a model, as well as to dispatch new messages based on user actions.
let view model dispatch =
    div [] [
        Navbar.navbar [ Navbar.Color IsPrimary ] [
            Navbar.Item.div [] [
                Heading.h1 [] [ str "Location Review!" ] ]
            ]
        
        Container.container [] [
            yield 
                Field.div [] [
                    Label.label [] [ str "Postcode" ]
                    Control.div [ Control.HasIconLeft; Control.HasIconRight ] [
                        Input.text
                            [ Input.Placeholder "Ex: EC2A 4NE"
                              Input.Value model.Postcode
                              Input.Modifiers [ Modifier.TextTransform TextTransform.UpperCase ]
                              Input.Color (if model.ValidationError.IsSome then Color.IsDanger else Color.IsSuccess)
                              Input.Props [ OnChange (fun ev -> dispatch (PostcodeChanged !!ev.target?value)); onKeyDown KeyCode.enter (fun _ -> dispatch GetReport) ] ]
                        Fulma.Icon.icon [ Icon.Size IsSmall; Icon.IsLeft ] [ Fa.i [ Fa.Solid.Home ] [] ]
                        (match model with
                         | { ValidationError = Some _ } -> 
                            Icon.icon [ Icon.Size IsSmall; Icon.IsRight ] [ Fa.i [ Fa.Solid.Exclamation ] [] ]
                         | { ValidationError = None } -> 
                            Icon.icon [ Icon.Size IsSmall; Icon.IsRight ] [ Fa.i [ Fa.Solid.Check ] [] ])
                    ]
                    Help.help
                       [ Help.Color (if model.ValidationError.IsNone then IsSuccess else IsDanger) ]
                       [ str (model.ValidationError |> Option.defaultValue "") ]
                ]
            yield
                Field.div [ Field.IsGrouped ] [
                    Level.level [ ] [
                        Level.left [] [
                            Level.item [] [
                                Button.button
                                    [ Button.IsFullWidth
                                      Button.Color IsPrimary
                                      Button.OnClick (fun _ -> dispatch GetReport)
                                      Button.Disabled (model.ValidationError.IsSome)
                                      Button.IsLoading (model.ServerState = ServerState.Loading) ]
                                    [ str "Submit" ] ] ]
                        Level.right [] [
                            Level.item [] [
                                Button.button
                                    [ Button.IsFullWidth
                                      Button.Color IsDark
                                      Button.OnClick (fun _ -> dispatch Clear)
                                      Button.Disabled (model.Postcode.Length = 0) ]
                                    [ str "Clear"] ] ] ]
                ]

            match model with
            | { Report = None; ServerState = (Idle | Loading) } -> ()
            | { ServerState = ServerError error } ->
                yield
                    Field.div [] [
                        Tag.list [ Tag.List.HasAddons; Tag.List.IsCentered ] [
                            Tag.tag [ Tag.Color Color.IsDanger; Tag.Size IsMedium ] [
                                str error
                            ]
                        ]
                    ]
            | { Report = Some model } ->
                yield
                    Tile.ancestor [ ] [
                        Tile.parent [ Tile.Size Tile.Is12 ] [
                            bingMapTile model.Location.Location.LatLong
                        ]
                    ]
                yield
                    Tile.ancestor [ ] [
                        Tile.parent [ Tile.IsVertical; Tile.Size Tile.Is4 ] [ 
                            locationTile model.Location
                            weatherTile model.Weather
                        ]
                        Tile.parent [ Tile.Size Tile.Is8 ] [
                            crimeTile model.Crimes
                        ]                   
                  ]        
        ]

        br [ ]

        Footer.footer [] [
            Content.content
                [ Content.Modifiers [ Fulma.Modifier.TextAlignment(Screen.All, TextAlignment.Centered) ] ]
                [ safeComponents ]
        ]
    ]