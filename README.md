# Backend API Endpoints för Maison Caillard

Detta dokument listar de rekommenderade API-endpoints som krävs för att ersätta nuvarande localStorage-hantering i Maison Caillard-applikationen, samt Stripe-integration för betalningar.

## Autentisering
*   **Customer**: Offentlig åtkomst för GET-anrop på produkter/meny/nyheter.
*   **Admin**: Kräver autentisering (t.ex. JWT) för alla POST/PUT/DELETE-anrop samt GET på beställningar.

---

## 1. Nyheter (News Items)
Hantera innehåll till sektionen med nyheter och erbjudanden.

| Metod | Endpoint | Beskrivning |
| :--- | :--- | :--- |
| `GET` | `/api/news` | Hämta alla nyhetsinlägg. |
| `POST` | `/api/news` | Skapa nytt nyhetsinlägg (Admin). Stöder bild (JPEG/PNG) via `multipart/form-data`. |
| `PUT` | `/api/news/:id` | Uppdatera nyhetsinlägg (Admin). Stöder byte av bild via `multipart/form-data`. |
| `DELETE` | `/api/news/:id` | Ta bort nyhetsinlägg och tillhörande bild (Admin). |

---

## 2. Menyn (Menu Items)
Hantera serveringsmenyn (kaffe, mackor etc. som köps på plats).

| Metod | Endpoint | Beskrivning |
| :--- | :--- | :--- |
| `GET` | `/api/menu` | Hämta hela menyn. |
| `POST` | `/api/menu` | Lägg till nytt menyval (Admin). Stöder bild (JPEG/PNG) via `multipart/form-data`. |
| `PUT` | `/api/menu/:id` | Uppdatera menyval (Admin). Stöder byte/uppdatering av bild. |
| `DELETE` | `/api/menu/:id` | Ta bort menyval och tillhörande bild (Admin). |

---

## 3. Beställningsbara Produkter (Products)
Hantera tårtor och bakverk som kan förbeställas.

| Metod | Endpoint | Beskrivning |
| :--- | :--- | :--- |
| `GET` | `/api/products` | Hämta alla produkter. |
| `POST` | `/api/products` | Skapa ny produkt (Admin). Stöder bild (JPEG/PNG) via `multipart/form-data`. |
| `PUT` | `/api/products/:id` | Uppdatera produkt (Admin). Stöder byte av bild. |
| `DELETE` | `/api/products/:id` | Ta bort produkt och tillhörande bild (Admin). |
| `PATCH` | `/api/products/:id/availability` | Snabbknapp för att växla `isAvailable` (Admin). |

## Bildhantering

`POST` och `PUT` för `/api/news`, `/api/menu` och `/api/products` behåller samma
`multipart/form-data`-kontrakt och fältnamnet `image`. JPEG, PNG och WebP godtas,
men formatet verifieras från filinnehållet och inte från filnamn eller klientens
`Content-Type`. En ogiltig eller för stor bild ger `400 Bad Request` som Problem
Details med titeln `Ogiltig bild`.

Alla nya bilder EXIF-roteras, skalas proportionerligt till högst 2400 px långsida
utan uppskalning och sparas som versionsunik lossy WebP. Kvaliteten justeras från
82 ned till den konfigurerade kvalitetsgränsen för att sikta på högst 200 KiB.
Transparens bevaras. `ImageUrl` har samma betydelse som tidigare men nya URL:er
slutar därför med `.webp`.

### Supabase Storage (produktion)

När `Supabase:ServiceRoleKey` är konfigurerad sparas bilder i **Supabase Storage**
istället för lokal disk. Databasen (Supabase PostgreSQL) lagrar bara URL-strängen;
själva filen ligger kvar i Storage även efter deploy av backend.

Publika bild-URL:er ser ut så här:

```txt
https://{project-ref}.supabase.co/storage/v1/object/public/uploads/{guid}.webp
```

**Setup i Supabase Dashboard:**

1. Gå till **Storage** → skapa bucket `uploads`
2. Sätt bucket till **Public** (så kundsidan kan ladda bilder utan inloggning)
3. Kopiera **service_role**-nyckeln under **Project Settings → API**
4. Sätt miljövariabler på Azure App Service:

```txt
Supabase__Url=https://{project-ref}.supabase.co
Supabase__ServiceRoleKey={din service_role key}
Supabase__StorageBucket=uploads
```

Utan Supabase-konfiguration faller backend tillbaka till lokal disk under
`wwwroot/uploads` (lämpligt för lokal utveckling).

**Efter att Storage är aktiverat:** ladda upp bilderna igen via admin för produkter,
meny och nyheter. Gamla URL:er som pekar mot Azure `/uploads/...` fungerar inte
längre förrän bilderna är uppladdade på nytt.

Gränserna kan ändras under `Uploads` i `appsettings.json`: maximal filstorlek,
bredd, höjd, pixelantal, utgående långsida, målstorlek och WebP-kvalitet. Varje
bearbetning loggar originalformat, originalstorlek, dimensioner, slutstorlek,
kvalitet och tid. Om målstorleken inte kan nås utan att gå under den tillåtna
kvalitetsgränsen sparas bilden och en varning loggas med den faktiska storleken.

Responsiva 640/1280/1920-varianter har övervägts men genereras inte i denna
version. Nuvarande API exponerar bara ett `ImageUrl`; att lägga till `srcset`-data
skulle ändra kontraktet och kräver en samordnad frontendändring. Primärbilden på
2400 px fungerar fortsatt i befintlig hero section.

### Engångsjobb för befintliga bilder

Kör följande med samma miljövariabler, databasanslutning, `App:BaseUrl` och
persistenta uploads-volym som produktionen:

```powershell
dotnet MaisonCalliard.Api.dll --optimize-existing-images
```

Jobbet läser endast bilder som refereras av NewsItems, MenuItems och Products.
För varje gammal fil skapas och färdigställs först en ny versionsunik WebP, sedan
uppdateras alla matchande `ImageUrl` i databasen, och först därefter raderas den
gamla filen. Vid databasfel återställs referenserna och den nya filen tas bort.
Redan optimerade WebP-filer under målstorleken hoppas över, så jobbet kan köras
igen säkert efter ett avbrott. Saknade eller externa filer loggas och lämnas
orörda. Ta ändå normal backup av databas och uploads-volym före produktionskörning.

---

## 4. Beställningar (Orders)
Hanterar kundens orderflöde.

| Metod | Endpoint | Beskrivning |
| :--- | :--- | :--- |
| `GET` | `/api/orders` | Hämta alla beställningar (Admin). |
| `POST` | `/api/orders` | Skapa en ny beställning (Customer/Backend). |
| `GET` | `/api/orders/:id` | Hämta detaljer för en specifik beställning. |
| `PATCH` | `/api/orders/:id/status` | Uppdatera status (`pending`, `completed`, `paid`). |
| `DELETE` | `/api/orders/:id` | Arkivera/ta bort en beställning (Admin). |

---

## 5. Inställningar (Settings)
Globala konfigurationer för appen.

| Metod | Endpoint | Beskrivning |
| :--- | :--- | :--- |
| `GET` | `/api/settings/lead-time` | Hämta nuvarande `leadTimeDays` (förbeställningstid). |
| `PUT` | `/api/settings/lead-time` | Uppdatera `leadTimeDays` (Admin). |

---

## 6. Betalningar (Stripe)
Integration för säker korthantering via Stripe.

| Metod | Endpoint | Beskrivning |
| :--- | :--- | :--- |
| `POST` | `/api/payments/create-session` | Skapa en [Stripe Checkout Session](https://stripe.com/docs/api/checkout/sessions). Skickar tillbaka en URL som kunden redirectas till. |
| `POST` | `/api/payments/webhook` | Webhook som Stripe anropar när betalningen är genomförd (`checkout.session.completed`). Här uppdateras orderstatus i databasen. |

### Flöde för betalning:
1.  Frontend skickar kundens val till `/api/payments/create-session`.
2.  Backend skapar en provisorisk order i databasen och initierar en Stripe Session.
3.  Frontend redirectar kunden till Stripes betalsida.
4.  Efter lyckad betalning skickar Stripe ett anrop till `/api/payments/webhook`.
5.  Backend verifierar webhook-signatur och markerar ordern som betald samt skickar ev. bekräftelsemejl.

---

## 7. Media/Bilder (File Management)
Bilder för Nyheter, Produkter och Menyval ska hanteras som **filer** (JPEG, PNG). Backend bör stödja `multipart/form-data` vid skapande/uppdatering.

| Funktion | Beskrivning |
| :--- | :--- |
| **Lägg till** | Sker via `POST` på respektive entitet med bildfil bifogad. |
| **Ändra** | Sker via `PUT` eller `PATCH` där en ny bildfil laddas upp och ersätter den gamla. |
| **Ta bort** | Sker automatiskt när entiteten raderas (`DELETE`), eller genom att uppdatera fältet till `null`/tom sträng. |

*Tips: Backend bör automatiskt städa bort gamla filer från lagringen när en bild byts ut eller tas bort.*

---

## 8. C# Entity Models (Proposals)
Eftersom backend ska byggas i **C#**, här är förslag på hur klasserna (Entities/DTOs) bör se ut för att matcha frontendens behov.

### Gemensamma Hjälpklasser
```csharp
public class LocalizedText 
{
    public string Se { get; set; }
    public string En { get; set; }
}

public class PriceOption 
{
    public string Label { get; set; }
    public decimal Price { get; set; }
}
```

### 1. NewsItem
```csharp
public class NewsItem 
{
    public string Id { get; set; }
    public LocalizedText Title { get; set; }
    public LocalizedText Subtitle { get; set; }
    public string ImageUrl { get; set; }
    public string? Link { get; set; }
}
```

### 2. MenuItem
```csharp
public enum MenuCategory { Sandwich, Pastry, Bread }

public class MenuItem 
{
    public string Id { get; set; }
    public LocalizedText Name { get; set; }
    public LocalizedText Description { get; set; }
    public MenuCategory Category { get; set; }
    public decimal Price { get; set; }
    public string ImageUrl { get; set; }
    public LocalizedText Ingredients { get; set; }
    public List<string> Allergies { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public double? TaxRate { get; set; }
}
```

### 3. Product (Beställningsbar)
```csharp
public enum ProductCategory { Cake, Pastry, Vegan, Season }
public enum CakeStyle { Tarte, Entremet }

public class Product 
{
    public string Id { get; set; }
    public LocalizedText Name { get; set; }
    public LocalizedText Description { get; set; }
    public ProductCategory Category { get; set; }
    public CakeStyle? Style { get; set; } // Nytt: För att skilja på t.ex. Tarte och Entremet
    public string ImageUrl { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsVegan { get; set; }
    public bool IsSeason { get; set; }
    public bool? BakedOnSite { get; set; }
    public bool? BakedThisMorning { get; set; }
    public Dictionary<string, int>? Stock { get; set; } // Key: "molndal" eller "jarntorget"
    public LocalizedText Ingredients { get; set; }
    public List<string> Allergies { get; set; }
    public List<PriceOption> PriceOptions { get; set; } // I frontend även kallat "sizes" ibland
    public double? TaxRate { get; set; }
}
```

### 4. Order & OrderDetails
```csharp
public enum OrderStatus { Pending, Completed, Paid }

public class CartItem 
{
    public string CartId { get; set; }
    public string ProductId { get; set; }
    public string Name { get; set; }
    public string? ImageUrl { get; set; }
    public string OptionLabel { get; set; }
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public class OrderDetails 
{
    public string Id { get; set; }
    public string StripeSessionId { get; set; } // För att matcha webhooken
    public List<CartItem> Items { get; set; }
    public decimal Total { get; set; }
    public decimal TaxAmount { get; set; } // Bra för bokföring
    public DateTime PickupDateTime { get; set; } // Använd DateTime istället för string för lättare sortering i C#
    public string Location { get; set; } 
    public string CustomerName { get; set; }
    public string Email { get; set; } // Behövs för Stripes kvitto och din bekräftelse
    public string Phone { get; set; }
    public string? Message { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsPrinted { get; set; } // För KitchenDashboard
    public DateTime CreatedAt { get; set; }
}
```

---

## Datamodell (Schema-tips)
Se `src/types.ts` i frontend-projektet för den senaste versionen av typerna. C#-objekten ovan bör serialiseras till JSON med **CamelCase** (standard i ASP.NET Core) för att matcha frontenden.


## Startup (Backend-konfiguration)
  ---


Solution: MaisonCalliard.sln — .NET 9 Web API with full DDD structure.

Architecture (4 layers):
- Domain — entities, value objects, enums, repository interfaces
- Application — services + DTOs per domain, IFileStorageService, IPaymentService
- Infrastructure — EF Core (SQL Server), 5 repositories, LocalFileStorageService (TODO stub), StripePaymentService
- Api — 6 controllers, JWT Bearer auth, Swagger, CORS

Before you can run it, update appsettings.json:
1. ConnectionStrings:DefaultConnection — your SQL Server connection string
2. Jwt:Key — replace with a real 32+ char secret
3. Stripe:SecretKey / Stripe:WebhookSecret — your Stripe keys

Then run: dotnet ef database update --project src/MaisonCalliard.Infrastructure --startup-project src/MaisonCalliard.Api

## Deploy med Netlify-frontend

Backend tillater bara frontend-domaner som finns i CORS-konfigurationen. Nar frontenden ligger pa Netlify behover backendens deploy-miljo darfor ha din Netlify-origin utan trailing slash:

```txt
Cors__AllowedOriginsCsv=https://din-site.netlify.app
```

Flera origins kan anges kommaseparerat, till exempel:

```txt
Cors__AllowedOriginsCsv=https://din-site.netlify.app,https://www.maisoncalliard.com
```

Satt backendens publika URL sa Stripe-redirects inte pekar mot localhost:

```txt
App__BaseUrl=https://din-backend-url
```

Satt Supabase Storage sa bilder sparas persistent (se avsnittet om bildhantering):

```txt
Supabase__Url=https://{project-ref}.supabase.co
Supabase__ServiceRoleKey={din service_role key}
Supabase__StorageBucket=uploads
```

I Netlify-frontenden ska API-basens miljovariabel peka mot samma backend-URL, inte mot `localhost`.

### Orderbekräftelse (Resend)

Efter lyckad betalning skickas ett HTML-mail via Resend (idempotent). Se [docs/order-receipt-testing.md](docs/order-receipt-testing.md) för test med Stripe CLI och Resend i testläge.
