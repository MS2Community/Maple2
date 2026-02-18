using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class NameValidatorTests {
    [Test]
    public void ValidName_ShouldReturnFalse() {
        // Valid names should return null (no error)
        Assert.That(NameValidator.ValidName("ValidName"), Is.True);
        Assert.That(NameValidator.ValidName("Test123"), Is.True);
    }

    [Test]
    public void EmptyName_ShouldReturnFalse() {
        // Names shorter than minimum should return name error
        Assert.That(NameValidator.ValidName(""), Is.False);
        Assert.That(NameValidator.ValidName(" "), Is.False);
    }

    [Test]
    public void InvalidCharacters_ShouldReturnFalse() {
        // Names with invalid characters should return name error
        Assert.That(NameValidator.ValidName("test@name"), Is.False);
        Assert.That(NameValidator.ValidName("name#test"), Is.False);
        Assert.That(NameValidator.ValidName("test$name"), Is.False);
        Assert.That(NameValidator.ValidName("test%name"), Is.False);
        Assert.That(NameValidator.ValidName("test*name"), Is.False);

        // Dashes and underscores are not allowed
        Assert.That(NameValidator.ValidName("User_Name"), Is.False);
        Assert.That(NameValidator.ValidName("Cool-Name"), Is.False);
    }

    [Test]
    public void OnlySpecialCharacters_ShouldReturnFalse() {
        // Names with only special characters should return name error
        Assert.That(NameValidator.ValidName("--"), Is.False);
        Assert.That(NameValidator.ValidName("__"), Is.False);
        Assert.That(NameValidator.ValidName("  "), Is.False);
        Assert.That(NameValidator.ValidName("-_-"), Is.False);
    }

    [Test]
    public void NullOrWhitespace_ShouldReturnFalse() {
        // Null or whitespace names should return name error
        Assert.That(NameValidator.ValidName(null!), Is.False);
        Assert.That(NameValidator.ValidName(""), Is.False);
        Assert.That(NameValidator.ValidName("   "), Is.False);
    }

    [Test]
    public void SpaceValidation_ShouldReturnFalse() {
        // Names with spaces should be invalid
        Assert.That(NameValidator.ValidName("Test Name"), Is.False);
        Assert.That(NameValidator.ValidName("Cool Player"), Is.False);
        Assert.That(NameValidator.ValidName("a b"), Is.False);

        // Names with leading or trailing spaces should be invalid
        Assert.That(NameValidator.ValidName(" ValidName"), Is.False);
        Assert.That(NameValidator.ValidName("ValidName "), Is.False);
        Assert.That(NameValidator.ValidName(" ValidName "), Is.False);

        // Names with multiple consecutive spaces should be invalid
        Assert.That(NameValidator.ValidName("Test  Name"), Is.False);
        Assert.That(NameValidator.ValidName("Test   Name"), Is.False);
        Assert.That(NameValidator.ValidName("A  B"), Is.False);
    }

    [Test]
    public void JapaneseCharacterNames_ShouldBeValid() {
        if (!Constant.AllowUnicodeInNames) {
            return;
        }
        // Japanese Hiragana characters should be valid
        Assert.That(NameValidator.ValidName("さくら"), Is.True);
        Assert.That(NameValidator.ValidName("ひろし"), Is.True);
        Assert.That(NameValidator.ValidName("あいうえお"), Is.True);

        // Japanese Katakana characters should be valid
        Assert.That(NameValidator.ValidName("サクラ"), Is.True);
        Assert.That(NameValidator.ValidName("ヒロシ"), Is.True);
        Assert.That(NameValidator.ValidName("アイウエオ"), Is.True);

        // Japanese Kanji characters should be valid
        Assert.That(NameValidator.ValidName("田中"), Is.True);
        Assert.That(NameValidator.ValidName("山田"), Is.True);
        Assert.That(NameValidator.ValidName("佐藤"), Is.True);
        Assert.That(NameValidator.ValidName("鈴木"), Is.True);

        // Mixed Japanese characters should be valid
        Assert.That(NameValidator.ValidName("さくら123"), Is.True);
        Assert.That(NameValidator.ValidName("田中ひろし"), Is.True);
        // Mixed with dashes/underscores should be invalid
        Assert.That(NameValidator.ValidName("サクラ_田中"), Is.False);
    }

    [Test]
    public void KoreanCharacterNames_ShouldBeValid() {
        if (!Constant.AllowUnicodeInNames) {
            return;
        }
        // Korean Hangul characters should be valid
        Assert.That(NameValidator.ValidName("김철수"), Is.True);
        Assert.That(NameValidator.ValidName("이영희"), Is.True);
        Assert.That(NameValidator.ValidName("박민수"), Is.True);
        Assert.That(NameValidator.ValidName("정수진"), Is.True);

        // Korean with numbers should be valid
        Assert.That(NameValidator.ValidName("김철수123"), Is.True);
        Assert.That(NameValidator.ValidName("이영희456"), Is.True);

        // Korean with dashes/underscores should be invalid
        Assert.That(NameValidator.ValidName("박민수-정"), Is.False);
        Assert.That(NameValidator.ValidName("정수진_김"), Is.False);
    }

    [Test]
    public void ChineseCharacterNames_ShouldBeValid() {
        if (!Constant.AllowUnicodeInNames) {
            return;
        }
        // Simplified Chinese characters should be valid
        Assert.That(NameValidator.ValidName("王小明"), Is.True);
        Assert.That(NameValidator.ValidName("李小红"), Is.True);
        Assert.That(NameValidator.ValidName("张三"), Is.True);
        Assert.That(NameValidator.ValidName("刘德华"), Is.True);

        // Traditional Chinese characters should be valid
        Assert.That(NameValidator.ValidName("王小明"), Is.True);
        Assert.That(NameValidator.ValidName("陳大文"), Is.True);
        Assert.That(NameValidator.ValidName("黃志強"), Is.True);
        Assert.That(NameValidator.ValidName("林美玲"), Is.True);

        // Chinese with numbers should be valid
        Assert.That(NameValidator.ValidName("王小明123"), Is.True);
        Assert.That(NameValidator.ValidName("李小红456"), Is.True);

        // Chinese with dashes/underscores should be invalid
        Assert.That(NameValidator.ValidName("张三-李四"), Is.False);
        Assert.That(NameValidator.ValidName("刘德华_陈"), Is.False);
    }

    [Test]
    public void SpecialCharacters_ShouldReturnFalse() {
        // Names with special or non-ASCII symbols should return name error
        Assert.That(NameValidator.ValidName("Name★"), Is.False);
        Assert.That(NameValidator.ValidName("Name."), Is.False);
        Assert.That(NameValidator.ValidName("Name!"), Is.False);
        Assert.That(NameValidator.ValidName("Name<3"), Is.False);
        Assert.That(NameValidator.ValidName("Name♪"), Is.False);
        Assert.That(NameValidator.ValidName("Name~"), Is.False);
        Assert.That(NameValidator.ValidName("Name*"), Is.False);
        Assert.That(NameValidator.ValidName("Name♥"), Is.False);
    }

    [Test]
    public void DashesAndUnderscores_ShouldReturnFalse() {
        Assert.That(NameValidator.ValidName("Name-Name"), Is.False);
        Assert.That(NameValidator.ValidName("Name_Name"), Is.False);
        Assert.That(NameValidator.ValidName("A-B_C-D"), Is.False);
    }

    [Test]
    public void AccentedNames_ShouldRespectAllowAccentsFlag() {
        if (!Constant.AllowUnicodeInNames) {
            return;
        }
        Assert.That(NameValidator.ValidName("José"), Is.True);
        Assert.That(NameValidator.ValidName("Renée"), Is.True);
        Assert.That(NameValidator.ValidName("Beyoncé"), Is.True);
        Assert.That(NameValidator.ValidName("José"), Is.True);
        Assert.That(NameValidator.ValidName("Renée"), Is.True);
        Assert.That(NameValidator.ValidName("Beyoncé"), Is.True);
        Assert.That(NameValidator.ValidName("André"), Is.True);
        Assert.That(NameValidator.ValidName("Zoë"), Is.True);
        Assert.That(NameValidator.ValidName("François"), Is.True);
    }
}
