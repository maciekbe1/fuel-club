using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RuleFragments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "RuleFragments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    Section = table.Column<string>(
                        type: "character varying(8)",
                        maxLength: 8,
                        nullable: false
                    ),
                    Heading = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Text = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    SearchText = table.Column<string>(
                        type: "character varying(2200)",
                        maxLength: 2200,
                        nullable: false
                    ),
                    SearchVector = table
                        .Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "simple")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "SearchText" }),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleFragments", x => x.Id);
                    table.CheckConstraint(
                        "CK_RuleFragments_SearchText",
                        "\"SearchText\" = lower(\"SearchText\")"
                    );
                    table.CheckConstraint(
                        "CK_RuleFragments_Section",
                        "\"Section\" ~ '^[1-9][0-9]?\\.[1-9][0-9]?$'"
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_RuleFragments_DocumentVersion_Section",
                table: "RuleFragments",
                columns: new[] { "DocumentVersion", "Section" },
                unique: true
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_RuleFragments_SearchVector",
                    table: "RuleFragments",
                    column: "SearchVector"
                )
                .Annotation("Npgsql:IndexMethod", "GIN");

            // regulamin-v1 as approved, frozen here: later changes to the parser or to normalization
            // must not change what this migration inserts (ProgramRulesSearchTests compares the rows
            // with the document parsed by current code). A later version gets its own migration.
            migrationBuilder.InsertData(
                table: "RuleFragments",
                columns: new[]
                {
                    "Id",
                    "DocumentVersion",
                    "Section",
                    "Heading",
                    "Text",
                    "SearchText",
                },
                values: new object[,]
                {
                    {
                        new Guid("01a20899-13a2-70d9-bdf0-6eb14e86563e"),
                        "regulamin-v1",
                        "1.1",
                        "Postanowienia ogólne",
                        "Fuel Club jest aplikacją demonstracyjną. Punkty, rabaty i nagrody służą wyłącznie symulacji.",
                        "postanowienia ogolne fuel club jest aplikacja demonstracyjna. punkty, rabaty i nagrody sluza wylacznie symulacji.",
                    },
                    {
                        new Guid("01a20899-13a2-7e4b-8769-b0a3b9b6a34d"),
                        "regulamin-v1",
                        "1.2",
                        "Postanowienia ogólne",
                        "Uczestnikiem jest osoba z kontem klienta w aplikacji Fuel Club.",
                        "postanowienia ogolne uczestnikiem jest osoba z kontem klienta w aplikacji fuel club.",
                    },
                    {
                        new Guid("01a20899-13a2-78a1-bf1b-e2810b30f6c1"),
                        "regulamin-v1",
                        "1.3",
                        "Postanowienia ogólne",
                        "Regulamin rozstrzyga o zasadach programu. Odpowiedzi asystenta czatu i konsultanta wyjaśniają zasady, ale ich nie zmieniają.",
                        "postanowienia ogolne regulamin rozstrzyga o zasadach programu. odpowiedzi asystenta czatu i konsultanta wyjasniaja zasady, ale ich nie zmieniaja.",
                    },
                    {
                        new Guid("01a20899-13a2-790e-8fb4-04580845224e"),
                        "regulamin-v1",
                        "2.1",
                        "Karta",
                        "Karta uczestnika ma postać kodu QR w aplikacji (zakładka „Karta”).",
                        "karta karta uczestnika ma postac kodu qr w aplikacji (zakladka „karta”).",
                    },
                    {
                        new Guid("01a20899-13a2-7d8b-ae1b-721411b6a5f0"),
                        "regulamin-v1",
                        "2.2",
                        "Karta",
                        "Kartę okazuje się w kasie przy zakupie paliwa, żeby otrzymać punkty.",
                        "karta karte okazuje sie w kasie przy zakupie paliwa, zeby otrzymac punkty.",
                    },
                    {
                        new Guid("01a20899-13a2-768f-9734-35d77c8078bc"),
                        "regulamin-v1",
                        "2.3",
                        "Karta",
                        "Kod karty wskazuje, komu przyznać punkty. Nie służy do logowania ani płatności.",
                        "karta kod karty wskazuje, komu przyznac punkty. nie sluzy do logowania ani platnosci.",
                    },
                    {
                        new Guid("01a20899-13a2-79b3-853f-f08a424cfecf"),
                        "regulamin-v1",
                        "2.4",
                        "Karta",
                        "Wymiana karty w aplikacji unieważnia poprzedni kod. Punkty i kupony zostają na koncie.",
                        "karta wymiana karty w aplikacji uniewaznia poprzedni kod. punkty i kupony zostaja na koncie.",
                    },
                    {
                        new Guid("01a20899-13a2-7857-b9d9-028d73b270c6"),
                        "regulamin-v1",
                        "3.1",
                        "Naliczanie punktów",
                        "Za każdy pełny 1 zł zapłacony za paliwo uczestnik dostaje 1 punkt. Grosze się nie liczą: 123,99 zł daje 123 pkt, a 0,99 zł - 0 pkt.",
                        "naliczanie punktow za kazdy pelny 1 zl zaplacony za paliwo uczestnik dostaje 1 punkt. grosze sie nie licza: 123,99 zl daje 123 pkt, a 0,99 zl - 0 pkt.",
                    },
                    {
                        new Guid("01a20899-13a2-78c8-b0f9-d5400a150284"),
                        "regulamin-v1",
                        "3.2",
                        "Naliczanie punktów",
                        "Punkty liczy się od kwoty zapłaconej po rabacie z kuponu, nie od ceny przed rabatem. Przykład: zakup 200,00 zł z rabatem 7,58 zł oznacza 192,42 zł do zapłaty i 192 pkt.",
                        "naliczanie punktow punkty liczy sie od kwoty zaplaconej po rabacie z kuponu, nie od ceny przed rabatem. przyklad: zakup 200,00 zl z rabatem 7,58 zl oznacza 192,42 zl do zaplaty i 192 pkt.",
                    },
                    {
                        new Guid("01a20899-13a2-7ccd-b77f-e630e175b2dd"),
                        "regulamin-v1",
                        "3.3",
                        "Naliczanie punktów",
                        "Zakup w całości pokryty rabatem (0 zł do zapłaty) nie daje punktów.",
                        "naliczanie punktow zakup w calosci pokryty rabatem (0 zl do zaplaty) nie daje punktow.",
                    },
                    {
                        new Guid("01a20899-13a2-742b-abf7-95eede27e0de"),
                        "regulamin-v1",
                        "3.4",
                        "Naliczanie punktów",
                        "Punkty przyznaje kasa przy rejestracji zakupu z okazaną kartą. Jeden paragon można zarejestrować tylko raz. Demo nie obsługuje dopisywania punktów po zakupie.",
                        "naliczanie punktow punkty przyznaje kasa przy rejestracji zakupu z okazana karta. jeden paragon mozna zarejestrowac tylko raz. demo nie obsluguje dopisywania punktow po zakupie.",
                    },
                    {
                        new Guid("01a20899-13a3-7912-8646-05cc84c59ceb"),
                        "regulamin-v1",
                        "3.5",
                        "Naliczanie punktów",
                        "Jeden zakup może wynosić najwyżej 5000 zł. Limit dotyczy kwoty przed rabatem.",
                        "naliczanie punktow jeden zakup moze wynosic najwyzej 5000 zl. limit dotyczy kwoty przed rabatem.",
                    },
                    {
                        new Guid("01a20899-13a3-79bf-89ac-ca912fc6c053"),
                        "regulamin-v1",
                        "3.6",
                        "Naliczanie punktów",
                        "Odebranie nagrody produktowej (np. kawy) nie daje punktów.",
                        "naliczanie punktow odebranie nagrody produktowej (np. kawy) nie daje punktow.",
                    },
                    {
                        new Guid("01a20899-13a3-7187-a1ea-16fad0aa1bea"),
                        "regulamin-v1",
                        "4.1",
                        "Wymiana punktów na kupon",
                        "W katalogu ofert są dwa rodzaje kuponów: - rabat na litr paliwa (stawka zł/l do limitu litrów); - nagroda produktowa (np. kawa, hot dog, myjnia).",
                        "wymiana punktow na kupon w katalogu ofert sa dwa rodzaje kuponow: - rabat na litr paliwa (stawka zl/l do limitu litrow); - nagroda produktowa (np. kawa, hot dog, myjnia).",
                    },
                    {
                        new Guid("01a20899-13a3-7e2d-b850-68e889cef48b"),
                        "regulamin-v1",
                        "4.2",
                        "Wymiana punktów na kupon",
                        "Wymiana jest możliwa, gdy saldo wystarcza na koszt oferty. Punkty schodzą z konta w chwili wymiany.",
                        "wymiana punktow na kupon wymiana jest mozliwa, gdy saldo wystarcza na koszt oferty. punkty schodza z konta w chwili wymiany.",
                    },
                    {
                        new Guid("01a20899-13a3-7b1c-9d0a-8c7065664894"),
                        "regulamin-v1",
                        "4.3",
                        "Wymiana punktów na kupon",
                        "Kupon zachowuje warunki z chwili wymiany. Późniejsza zmiana lub wyłączenie oferty go nie dotyczy.",
                        "wymiana punktow na kupon kupon zachowuje warunki z chwili wymiany. pozniejsza zmiana lub wylaczenie oferty go nie dotyczy.",
                    },
                    {
                        new Guid("01a20899-13a3-7f71-9cec-d8de65707556"),
                        "regulamin-v1",
                        "4.4",
                        "Wymiana punktów na kupon",
                        "Demo nie obsługuje anulowania wymiany ani zwrotu punktów.",
                        "wymiana punktow na kupon demo nie obsluguje anulowania wymiany ani zwrotu punktow.",
                    },
                    {
                        new Guid("01a20899-13a3-7ecb-a859-846ed9edbb67"),
                        "regulamin-v1",
                        "5.1",
                        "Aktywacja i odbiór kuponu",
                        "Kupon po wymianie ma stan „Do aktywacji”. Aktywacja w aplikacji (albo w czacie, po potwierdzeniu) nadaje mu kod QR.",
                        "aktywacja i odbior kuponu kupon po wymianie ma stan „do aktywacji”. aktywacja w aplikacji (albo w czacie, po potwierdzeniu) nadaje mu kod qr.",
                    },
                    {
                        new Guid("01a20899-13a3-7b08-8c1e-d57eb958df6c"),
                        "regulamin-v1",
                        "5.2",
                        "Aktywacja i odbiór kuponu",
                        "Aktywacji nie można cofnąć. Aktywny kupon czeka na odbiór bez terminu.",
                        "aktywacja i odbior kuponu aktywacji nie mozna cofnac. aktywny kupon czeka na odbior bez terminu.",
                    },
                    {
                        new Guid("01a20899-13a3-7da0-8fc4-545714c69a60"),
                        "regulamin-v1",
                        "5.3",
                        "Aktywacja i odbiór kuponu",
                        "Kupon jest jednorazowy. Po odbiorze ma stan „Wykorzystany”.",
                        "aktywacja i odbior kuponu kupon jest jednorazowy. po odbiorze ma stan „wykorzystany”.",
                    },
                    {
                        new Guid("01a20899-13a3-7ca7-8b69-051e82eefdc3"),
                        "regulamin-v1",
                        "5.4",
                        "Aktywacja i odbiór kuponu",
                        "Kupon rabatowy: - realizuje się przy zakupie paliwa, okazując kartę i kod kuponu; - rabat to stawka × litry, najwyżej do limitu litrów z kuponu; wynik zaokrągla się do groszy (połówki w górę), a dopiero potem ogranicza do kwoty zakupu; - do jednego zakupu można użyć jednego kuponu.",
                        "aktywacja i odbior kuponu kupon rabatowy: - realizuje sie przy zakupie paliwa, okazujac karte i kod kuponu; - rabat to stawka × litry, najwyzej do limitu litrow z kuponu; wynik zaokragla sie do groszy (polowki w gore), a dopiero potem ogranicza do kwoty zakupu; - do jednego zakupu mozna uzyc jednego kuponu.",
                    },
                    {
                        new Guid("01a20899-13a3-7e81-8bbc-42f0a2267376"),
                        "regulamin-v1",
                        "5.5",
                        "Aktywacja i odbiór kuponu",
                        "Nagrodę produktową kasjer wydaje bez tankowania, po okazaniu karty i kodu kuponu.",
                        "aktywacja i odbior kuponu nagrode produktowa kasjer wydaje bez tankowania, po okazaniu karty i kodu kuponu.",
                    },
                    {
                        new Guid("01a20899-13a3-7e11-9876-e2e8106f9fe3"),
                        "regulamin-v1",
                        "5.6",
                        "Aktywacja i odbiór kuponu",
                        "Kuponu rabatowego nie można wymienić na nagrodę, a nagrody - na rabat.",
                        "aktywacja i odbior kuponu kuponu rabatowego nie mozna wymienic na nagrode, a nagrody - na rabat.",
                    },
                    {
                        new Guid("01a20899-13a3-7610-8f20-bf246f6e8a5f"),
                        "regulamin-v1",
                        "6.1",
                        "Ważność",
                        "Punkty nie wygasają.",
                        "waznosc punkty nie wygasaja.",
                    },
                    {
                        new Guid("01a20899-13a3-7c29-b79e-985662b476b6"),
                        "regulamin-v1",
                        "6.2",
                        "Ważność",
                        "Kupony (nieaktywowane i aktywne) nie mają terminu ważności.",
                        "waznosc kupony (nieaktywowane i aktywne) nie maja terminu waznosci.",
                    },
                    {
                        new Guid("01a20899-13a3-76a2-a4df-dffbc34436aa"),
                        "regulamin-v1",
                        "7.1",
                        "Asystent czatu",
                        "Asystent odpowiada na podstawie tego regulaminu i danych konta uczestnika: salda, kuponów i ofert.",
                        "asystent czatu asystent odpowiada na podstawie tego regulaminu i danych konta uczestnika: salda, kuponow i ofert.",
                    },
                    {
                        new Guid("01a20899-13a3-72e1-8ffd-20180a79ddb2"),
                        "regulamin-v1",
                        "7.2",
                        "Asystent czatu",
                        "Asystent nie przyznaje ani nie odejmuje punktów. Aktywuje kupon tylko po potwierdzeniu przez uczestnika.",
                        "asystent czatu asystent nie przyznaje ani nie odejmuje punktow. aktywuje kupon tylko po potwierdzeniu przez uczestnika.",
                    },
                    {
                        new Guid("01a20899-13a3-78b5-891a-7bdeca1b9b7b"),
                        "regulamin-v1",
                        "7.3",
                        "Asystent czatu",
                        "Gdy regulamin nie daje odpowiedzi, asystent mówi to wprost i proponuje rozmowę z konsultantem.",
                        "asystent czatu gdy regulamin nie daje odpowiedzi, asystent mowi to wprost i proponuje rozmowe z konsultantem.",
                    },
                    {
                        new Guid("01a20899-13a3-7092-aafb-901d73d7a7e0"),
                        "regulamin-v1",
                        "8.1",
                        "Konsultant",
                        "Rozmowę z konsultantem rozpoczyna się z czatu, po zgodzie na zapisanie i przechowywanie rozmowy.",
                        "konsultant rozmowe z konsultantem rozpoczyna sie z czatu, po zgodzie na zapisanie i przechowywanie rozmowy.",
                    },
                    {
                        new Guid("01a20899-13a3-7a15-87e4-d39eaa000461"),
                        "regulamin-v1",
                        "8.2",
                        "Konsultant",
                        "Do zgłoszenia można dołączyć wcześniejszą rozmowę z asystentem. To wybór uczestnika.",
                        "konsultant do zgloszenia mozna dolaczyc wczesniejsza rozmowe z asystentem. to wybor uczestnika.",
                    },
                    {
                        new Guid("01a20899-13a3-72cd-8c19-e45a602e4ef5"),
                        "regulamin-v1",
                        "8.3",
                        "Konsultant",
                        "Konsultant pomaga i wyjaśnia zasady oraz informacje przekazane przez uczestnika. Nie ma dostępu do salda ani kuponów i ich nie zmienia.",
                        "konsultant konsultant pomaga i wyjasnia zasady oraz informacje przekazane przez uczestnika. nie ma dostepu do salda ani kuponow i ich nie zmienia.",
                    },
                    {
                        new Guid("01a20899-13a3-7351-b725-6c3550f70379"),
                        "regulamin-v1",
                        "9.1",
                        "Zmiany regulaminu",
                        "Regulamin ma numer wersji i datę. Asystent podaje wersję, na której opiera odpowiedź.",
                        "zmiany regulaminu regulamin ma numer wersji i date. asystent podaje wersje, na ktorej opiera odpowiedz.",
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RuleFragments");

            migrationBuilder
                .AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
