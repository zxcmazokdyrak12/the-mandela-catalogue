// Text that lives on Mark's laptop in night 2: the decrypted county archive, the chat with the man who called,
// and a few pages the browser can open.
public static class LaptopContent
{
    public struct DFile { public string name, size, body; }

    // unlocked one by one as decryption passes 20 / 40 / 60 / 80 / 100 %
    public static readonly DFile[] Files =
    {
        new DFile { name = "SHERIFF_REPORT_0912.txt", size = "2 KB", body =
            "MANDELA COUNTY SHERIFF'S OFFICE\nINCIDENT REPORT No. 0912\n\n" +
            "Call: 14 Hollow Creek Road. 2:51 AM.\n" +
            "Caller is a neighbor. At about 2:40 AM he heard the woman of the house on her porch, calling her husband by name.\n" +
            "The husband died in March.\n\n" +
            "On arrival: front door open. Lights on. Beds made.\n" +
            "No signs of a struggle. Missing: 3 persons (one woman, two children).\n\n" +
            "Note: the house phone line was disconnected in May. The neighbor states he heard the phone ringing inside the house that night." },

        new DFile { name = "MISSING_SEP-OCT.csv", size = "1 KB", body =
            "NAME                  DATE    WHERE        LAST CONTACT\n" +
            "Whitaker, Daniel      09/03   home         call from his late mother\n" +
            "Whitaker, Ruth        09/03   home         -\n" +
            "Kowalski, Anna        09/11   home         \"my husband is back from work\"\n" +
            "Pierce, Thomas        09/19   home         opened the door to his brother\n" +
            "Gray, Ellis (16)      09/27   bedroom      \"someone is knocking, it's dad\"\n" +
            "Alvarez, R. (deputy)  10/04   on a call    -\n" +
            "...8 more rows corrupted...\n\n" +
            "In every case: the door was opened from the inside." },

        new DFile { name = "911_TRANSCRIPT_1004.txt", size = "3 KB", body =
            "DISPATCHER: 911, what is your emergency?\n" +
            "CALLER: He's standing outside my door. He's knocking.\n" +
            "DISPATCHER: Who is outside your door, ma'am?\n" +
            "CALLER: My brother. My brother died last year.\n" +
            "DISPATCHER: Ma'am, do not open the door. A patrol car is already...\n" +
            "CALLER: He says he's cold. He knows what they called me when I was little.\n" +
            "CALLER: He says if I hide, he'll find me anyway.\n" +
            "DISPATCHER: Ma'am? Ma'am, can you hear me?\n" +
            "[knocking. a long sound of a door opening]\n" +
            "MALE VOICE: Thank you.\n" +
            "[call disconnected 2:58 AM]" },

        new DFile { name = "PHOTO_0047.jpg.txt", size = "1 KB", body =
            "Image damaged. 61% recovered.\n\n" +
            "An upstairs bedroom. The bed is unmade. A laptop on the nightstand, screen on.\n" +
            "Newspapers.\n\n" +
            "The picture was taken from inside the room, at floor level, roughly from under the desk.\n" +
            "Time taken: 3:12 AM.\n\n" +
            "There was no camera in the house." },

        new DFile { name = "REPLACEMENT_LIST.txt", size = "1 KB", body =
            "LIST - REPLACEMENT QUEUE\n\n" +
            "Whitaker, D. ............ REPLACED\n" +
            "Kowalski, A. ............ REPLACED\n" +
            "Pierce, T. .............. REPLACED\n" +
            "Gray, E. ................ REPLACED\n" +
            "HEATHCLIFF, MARK ........ night 2 - IN PROGRESS\n\n" +
            "Contact: telephone. Instruction: convince him to open the files.\n" +
            "While he is looking at the screen, he does not hear the door." },
    };

    public struct Msg { public bool mine; public string text; public float at; }   // at = decryption progress when it arrives

    public static readonly Msg[] Chat =
    {
        new Msg { mine = false, at = -1f, text = "Mark. It's me, I called you last night." },
        new Msg { mine = false, at = -1f, text = "I sent you the county archive. It's encrypted." },
        new Msg { mine = false, at = -1f, text = "Run the decryption and don't leave the laptop." },
        new Msg { mine = true,  at = -1f, text = "What's in it?" },
        new Msg { mine = false, at = -1f, text = "Everything they're hiding. Who went missing. Who came back." },
        new Msg { mine = false, at = -1f, text = "If they knock: SHUT DOWN the laptop first, then get under the desk. And no light." },
        new Msg { mine = false, at = 0.12f, text = "They see the screen glowing through the gap under the door." },
        new Msg { mine = false, at = 0.14f, text = "They always check the bed first." },
        new Msg { mine = false, at = 0.38f, text = "Did you see the papers on your nightstand? Those are only the ones they found." },
        new Msg { mine = true,  at = 0.58f, text = "There's a 911 call in here. A woman says her brother is at the door." },
        new Msg { mine = false, at = 0.61f, text = "He was at the door. It just wasn't him." },
        new Msg { mine = false, at = 0.82f, text = "Have you opened the last file yet?" },
        new Msg { mine = false, at = 1.0f,  text = "..." },
        new Msg { mine = false, at = 1.01f, text = "Mark." },
        new Msg { mine = false, at = 1.02f, text = "I didn't call you last night." },
    };

    // ------------------------------------------------------------------ browser
    public struct Link { public string text, to; }
    public struct Page { public string id, url, title, heading, body; public Link[] links; public bool ads; }

    public static readonly Page[] Pages =
    {
        new Page { id = "home", url = "http://www.mandelacountygazette.com/", title = "Mandela County Gazette - Online Edition", heading = "MANDELA COUNTY GAZETTE",
            body = "Online Edition  ·  Serving Mandela County since 1911\n\nTOP STORIES",
            links = new[] {
                new Link { text = "Third family reported missing from Hollow Creek Road", to = "hollow" },
                new Link { text = "Sheriff's Office: \"Do not answer the door after dark\"", to = "sheriff" },
                new Link { text = "Missing persons - updated list", to = "missing" },
                new Link { text = "Civil Defense advisory for all residents", to = "civil" } },
            ads = true },

        new Page { id = "hollow", url = "http://www.mandelacountygazette.com/news/hollow-creek", title = "Third family missing - Gazette", heading = "THIRD FAMILY REPORTED MISSING",
            body = "Deputies were called to Hollow Creek Road early Tuesday after a neighbor heard a woman calling for her late husband.\n\n" +
                   "The house was found open with the lights on. Nobody was inside.\n\n" +
                   "\"The beds were made,\" one deputy said. \"Like nobody had slept there at all.\"\n\n" +
                   "It is the third such case this month. The Sheriff's Office declined to comment on reports that the house phone, disconnected since May, was heard ringing.",
            links = new[] { new Link { text = "< Back to front page", to = "home" } } },

        new Page { id = "sheriff", url = "http://www.mandelacountygazette.com/news/sheriff-statement", title = "Sheriff's statement - Gazette", heading = "\"DO NOT ANSWER THE DOOR\"",
            body = "The Mandela County Sheriff's Office asks residents to keep doors and windows locked at night.\n\n" +
                   "If someone you know comes to your home after dark, do not let them in - even if they call you by name, even if they say they are hurt.\n\n" +
                   "Do not look at them for long. Do not speak to them.\n\n" +
                   "If you hear knocking inside your home, hide and stay quiet until morning.",
            links = new[] { new Link { text = "< Back to front page", to = "home" } } },

        new Page { id = "missing", url = "http://www.mandelacountygazette.com/missing", title = "Missing persons - Gazette", heading = "MISSING PERSONS",
            body = "Whitaker, Daniel  ·  Whitaker, Ruth  ·  Kowalski, Anna  ·  Pierce, Thomas  ·  Gray, Ellis (16)  ·  Alvarez, R.\n\n" +
                   "If you have seen any of these people since the date they went missing, call the Sheriff's Office.\n\n" +
                   "Do NOT approach them. Do NOT let them into your home.",
            links = new[] { new Link { text = "< Back to front page", to = "home" } }, ads = true },

        new Page { id = "civil", url = "http://www.mandelacounty.gov/civil-defense", title = "Mandela County Civil Defense", heading = "CIVIL DEFENSE ADVISORY",
            body = "THIS IS NOT A TEST.\n\n" +
                   "Reports of persons imitating residents of Mandela County continue.\n\n" +
                   "They may look like your family. They may sound like your family. They may know things only your family would know.\n\n" +
                   "They are not your family.\n\n" +
                   "Stay inside. Keep the lights off in rooms with windows. Do not open the door.",
            links = new[] { new Link { text = "< Back to front page", to = "home" } } },
    };

    public static int PageIndex(string id)
    {
        for (int i = 0; i < Pages.Length; i++) if (Pages[i].id == id) return i;
        return 0;
    }
}
