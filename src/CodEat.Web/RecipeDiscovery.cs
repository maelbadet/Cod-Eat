namespace CodEat.Web;

public static class RecipeDiscovery
{
    // Classify listed ingredients, never recipe titles or suggested substitutions.
    private static readonly Dictionary<string,string[]> Proteins = new()
    {
        ["Poulet"]=["poulet"], ["Dinde"]=["dinde"], ["Bœuf"]=["boeuf"],
        ["Porc"]=["porc","jambon","lardon"], ["Agneau"]=["agneau"], ["Canard"]=["canard"],
        ["Veau"]=["veau"], ["Lapin"]=["lapin"],
        ["Saumon"]=["saumon"], ["Cabillaud"]=["cabillaud"], ["Thon"]=["thon"],
        ["Sardine"]=["sardine"], ["Maquereau"]=["maquereau"], ["Truite"]=["truite"],
        ["Colin / merlu"]=["colin","merlu"], ["Crevettes"]=["crevette"],
        ["Autres produits de la mer"]=["moule","calamar","seiche","lieu","dorade","poisson"],
        ["Œufs"]=["oeuf"], ["Tofu"]=["tofu"], ["Légumineuses"]=["lentille","pois chiche","haricot","pois casse"]
    };
    public static string[] ProteinKinds(Recipe recipe)
    {
        var ingredients=recipe.Ingredients.Select(x=>RecipePageParser.Normalize(x.Canonical).Replace("œ","oe")).ToArray();
        return Proteins.Where(group=>group.Value.Any(word=>ingredients.Any(name=>
            name.Split(' ', '-', '\'', ',', '(', ')').Any(token=>token==word || token==word+"s")
            || word.Contains(' ') && name.Contains(word,StringComparison.Ordinal))))
            .Select(x=>x.Key).ToArray();
    }
    public static int PageSize(int value)=>value is 12 or 24 or 48 ? value : 12;
}
