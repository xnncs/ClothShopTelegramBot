namespace ShopTelegramBot.Abstract;

public interface ICallbackGenerateHelper
{
    string GenerateCategoriesCallbackFormatStringOnDelete(string x);
    string GenerateCategoriesCallbackFormatStringOnGet(string x);
    string GenerateItemsCallbackFormatStringOnGet(string x);
    string GenerateItemsCallbackFormatStringOnDelete(string x);
}