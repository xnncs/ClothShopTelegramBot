using System.Text.RegularExpressions;

namespace ShopTelegramBot.Abstract;

public interface ICallbackGenerateHelper
{
    string GenerateCategoriesCallbackFormatStringOnDelete(string x);
    string GenerateCategoriesCallbackFormatStringOnGet(string x);
    string GenerateItemsCallbackFormatStringOnGet(string x);
    string GenerateItemsCallbackFormatStringOnDelete(string x);
    string GenerateCallbackOnAddToCart(string x);
    string GenerateCallbackOnRemoveFromCart(string x);

    string GenerateCallbackOnGetFeedbackByPageNumber(int pageNumber);
    Regex GetOnGetFeedbackByPageNumberRegex();
    int GetPageNumberByGetFeedbackCallbackString(string x);
}