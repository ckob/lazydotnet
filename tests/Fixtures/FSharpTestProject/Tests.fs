namespace FSharpTestProject

open Xunit

module Tests =
    [<Fact>]
    let ``Test 1`` () =
        Assert.Equal(4, 2 + 2)
