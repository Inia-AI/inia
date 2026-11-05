using IniaAi.Common.Enums;
using System.Security.Cryptography;
using System.Text;

namespace IniaAi.Common.DataMasking;

public class StringReplacements
{
    public readonly List<string> _replacementFunds = [.. new List<string>
    {
        "Eryops", "Maltipoo", "Jackdaw", "Chamois", "Hallucigenia", "Dog", "Merganser", "Airedoodle", "Caterpillar",
        "Herrerasaurus", "Bobcat", "Labahoula", "Dorgi", "Surgeonfish", "Basilosaurus", "Sphynx", "Stromatolite",
        "Rattlesnake", "Goldendoodle", "Starfish", "Pomchi", "Babirusa", "Caracal", "Insects", "Sarcosuchus", "Rhinoceros",
        "Jellyfish", "Barnevelder", "Drever", "Vizsla", "Chimpanzee", "Lion", "Stoat", "Gibbon", "Dreadnoughtus", "Centipede",
        "Hokkaido", "Dachsador", "Turaco", "Damselfish", "Andrewsarchus", "Meagle", "Boerboel", "Ragamuffin", "Porcupine", "Locust",
        "Cheagle", "Diprotodon", "Carp", "Tortoise", "Sculpin", "Xenacanthus", "Polecat", "Shepkita", "Klipspringer", "Magpie",
        "Camel", "Schipperke", "Tylosaurus", "Grunion", "Samoyed", "Gazelle", "Macrauchenia", "Binturong", "Egret", "Bullsnake",
        "Groenendael", "Loach", "Tarpon", "Nematode", "Mallard", "Roadrunner", "Chinchilla", "Ceratosaurus", "Squirrelfish", "Ragdoll",
        "Parrotfish", "Chimaera", "Pudelpointer", "Chiton", "Turnspit", "Miki", "Chipit", "Chorkie", "Corgipoo", "Therizinosaurus",
        "Saiga", "Coryphodon", "Peekapoo", "Pachycephalosaurus", "Towhee", "Kagu", "Eskipoo", "Brug", "Dragonfly", "Lumpfish", "Eelpout",
        "Armadillo", "Ocelot", "Glechon", "Grouse", "Lowchen", "Hellbender", "Titanoboa", "Avocet", "Raccoon", "Pitador", "Bonobo", "Viper",
        "Bordoodle", "Mosquito", "Desmostylus", "Hare", "Loris", "Akita", "Somali", "Hovasaurus", "Stingray", "Chicken", "Toxodon", "Liger",
        "Griffonshire", "Caribou", "Pumi", "Shiranian", "Sapsali", "Ladybug", "Porcupinefish", "Phytosaurs", "Sparrow", "Microraptor", "Macaw",
        "Deinosuchus", "Daug", "Bulldog", "Meiolania", "Barnacle", "Cantil", "Yabby", "Dickcissel", "Armyworm", "Husky", "Parrot", "Phorusrhacos",
        "Bullfrog", "Nyala", "Rockfish", "Havashu", "Bass", "Markhor", "Chusky", "Quokka", "Crocodylomorph", "Tuatara", "Cockle", "Cavador",
        "Neanderthal", "Pyrosome", "Ibex", "Wryneck", "Linnet", "Cockroach", "Wiwaxia", "Ornithocheirus", "Batfish", "Escolar", "Tamarin",
        "Ainu", "Megalochelys", "Mudi", "Pitsky", "Nautilus", "Krait", "Pangolin", "Sinosauropteryx", "Zebu", "Gopher", "Dingo", "Penguin",
        "Frug", "Kooikerhondje", "Xiongguanlong", "Worm", "Chartreux", "Chipoo", "Knifefish", "Waterbuck", "Termite", "Goldador", "Mamba",
        "Archaeotherium", "Stork", "Spinosaurus", "Dalmadoodle", "Hamster", "Boxachi", "Tang", "Suchomimus", "Crab", "Hartebeest", "Beago",
        "Quail", "Shastasaurus", "Trout", "Veery", "Sandpiper", "Ferret", "Kingfisher", "Toadfish", "Insect", "Allosaurus", "Elk", "Frengle",
        "Fisher", "Stonefish", "Poochon", "Eurypterus", "Gomphotherium", "Grasshopper", "Cuttlefish", "Bichpoo", "Gharial", "Pelagornithidae",
        "Asp", "Beauceron", "Ladyfish", "Bonefish", "Corkie", "Huskita", "Suckerfish", "Labmaraner", "Badger", "Briard", "Corgidor", "Frog", "Jaguar",
        "Wombat", "Tick", "Douc", "Weimaraner", "Bea-Tzu", "Morkie", "Tiktaalik", "Xiaotingia", "Lizardfish", "Swallow", "Toucan", "Quagga", "Chipmunk",
        "Havamalt", "Lancetfish", "Pterodactyl", "Hatzegopteryx", "Peagle", "Argentinosaurus", "Okapi", "Caecilian", "Nuthatch", "Mole", "Grebe", "Maiasaura",
        "Salamander", "Firefly", "Arctodus", "Pinfish", "Hyaenodon", "Papillon", "Triggerfish", "Cow", "Angelshark", "Ceratopsian", "Lamprey", "Polacanthus",
        "Echidna", "Wasp", "Whiting", "Chickadee", "Squirrel", "Gorilla", "Orangutan", "Abyssinian", "Sivatherium", "Whoodle", "Halibut", "Eurasier",
        "Budgerigar", "Thylacosmilus", "Whimbrel", "Hyena", "Tetra", "Goose", "Yoranian", "Lynx", "Octopus", "Elasmosaurus", "Megatherium", "Gollie", "Dugong",
        "Sloth", "Cardinal", "Wallaby", "Crow", "Parakeet", "Lurcher", "Pigeon", "Cassowary", "Havashire", "Spanador", "Milkfish", "Liopleurodon", "Dimetrodon",
        "Jack-Chi", "Skua", "Paleoparadoxia", "Platypus", "Nabarlek", "Alusky", "Xenoceratops", "Chihuahua", "Quoll", "Bullmastiff", "Springbok", "Dalmatian",
        "Australopithecus", "Mudskipper", "Havanese", "Tuna", "Pug", "Weimardoodle", "Maggot", "Seal", "Genet", "Liopleurodon", "Sailfish", "Arctotherium",
        "Ostracod", "Ornithomimus", "Indri", "Flamingo", "Maltese", "Kudu", "Limpet", "Fly", "Wrasse", "Olm", "Heron", "Megalodon", "Newfypoo", "Taipan",
        "Dinopithecus", "Barinasuchus", "Tarantula", "Hornet", "Kishu", "Boxfish", "Oarfish", "Lemur", "Boas", "Molly", "Xenoposeidon", "Peacock", "Osprey",
        "Dik-Dik", "Swan", "Brittany", "Alpaca", "Eel", "Butterfly", "Earthworm", "Canvasback", "Cockatiel", "Thanatosdrakon", "Agouti", "Beagle", "Siberpoo",
        "Bird", "Pugapoo", "Shollie", "Barosaurus", "Monkfish", "Sauropoda", "Labraheeler", "Snorkie", "Haddock", "Otter", "Bernedoodle", "Boskimo", "Nightingale",
        "Tusoteuthis", "Elasmotherium", "Zorse", "Panther", "Warthog", "Labradane", "Nuralagus", "Copperhead", "Blobfish", "Kingklip", "Balinese", "Utonagan"
    }.OrderBy(s => s.Length)];

    private readonly List<string> _replacementCountries = [.. new List<string>
    {
        "Nauru", "Palau", "Tuvalu", "Niue", "Tonga", "Malta", "Samoa", "Fiji", "Cyprus", "Vanuatu",
        "Bahrain", "Comoros", "Jamaica", "Lebanon", "Mauritius", "Singapore", "Dominica", "Maldives",
        "Micronesia", "Seychelles", "Barbados", "Swaziland", "Kiribati", "Grenada", "Saint Lucia",
        "Cape Verde", "Belize", "Bahamas", "Timor-Leste", "Montenegro", "Djibouti", "Luxembourg",
        "Suriname", "Slovenia", "Macedonia", "Mauritania", "Equatorial Guinea", "Trinidad and Tobago",
        "Estonia", "Burundi", "Lesotho", "Armenia", "Albania", "Liberia", "Georgia", "Gambia",
        "Croatia", "Bhutan", "Tunisia", "Slovakia", "Sierra Leone", "Denmark", "Israel", "Austria",
        "Serbia", "Jordan", "Ireland", "Armenia", "Bulgaria", "Portugal", "Switzerland", "Azerbaijan",
        "Honduras", "Liberia", "Hungary", "Belarus", "Tajikistan", "Greece", "Nicaragua", "Eritrea",
        "North Korea", "South Korea", "Benin", "Malawi", "Rwanda", "Somalia", "Zimbabwe", "Guinea",
        "Cambodia", "Senegal", "Bolivia", "Haiti", "Belgium", "Tunisia", "Cuba", "South Sudan",
        "Dominican Republic", "Czech Republic", "Greece", "Portugal", "Jordan", "United Arab Emirates",
        "Finland", "Norway", "Ivory Coast", "Sri Lanka", "Cameroon", "Netherlands", "Thailand",
        "North Macedonia", "Burkina Faso", "Madagascar", "Taiwan", "Niger", "Zimbabwe"
    }.OrderBy(s => s.Length)];

    private readonly List<string> _replacementUrls = [.. new List<string>
    {
        "bit.ly", "goo.gl", "tinyurl.com", "ow.ly", "t.co", "lnkd.in", "db.tt", "qr.ae", "adf.ly",
        "moourl.com", "snipurl.com", "short.to", "tny.im", "twurl.nl", "ur1.ca", "cli.gs", "tiny.cc",
        "zi.ma", "spoti.fi", "sptfy.com", "amzn.to", "tcrn.ch", "ift.tt", "fb.me", "nbcnews.to",
        "theatln.tc", "nyti.ms", "cnn.it", "wapo.st", "on.wsj.com", "nyp.st", "bbc.in", "reut.rs",
        "politi.co", "econ.st", "on.mktw.net", "bloom.bg", "foxs.pt", "yhoo.it", "kck.st", "flip.it",
        "ed.gr", "wh.gov", "go.nasa.gov", "1.usa.gov", "n.pr", "to.pbs.org", "nydn.us", "lat.ms",
        "usat.ly", "huff.to", "aje.io", "fxn.ws", "abcn.ws", "thebea.st", "ti.me", "rol.st", "elnuevodia.com",
        "on.today.com", "nbcnews.to", "nypost.com", "washingtonpost.com", "wsj.com", "bloomberg.com",
        "reuters.com", "apnews.com", "nytimes.com", "theguardian.com", "bbc.com", "cnn.com", "foxnews.com",
        "https://bit.ly", "https://goo.gl", "https://tinyurl.com", "https://ow.ly", "https://t.co",
        "https://lnkd.in", "https://db.tt", "https://qr.ae", "https://adf.ly", "https://moourl.com",
        "https://snipurl.com", "https://short.to", "https://tny.im", "https://twurl.nl", "https://ur1.ca",
        "https://cli.gs", "https://tiny.cc", "https://zi.ma", "https://spoti.fi", "https://sptfy.com",
        "https://amzn.to", "https://tcrn.ch", "https://ift.tt", "https://fb.me", "https://nbcnews.to",
        "https://theatln.tc", "https://nyti.ms", "https://cnn.it", "https://wapo.st", "https://on.wsj.com",
        "https://nyp.st", "https://bbc.in", "https://reut.rs", "https://politi.co", "https://econ.st",
        "https://on.mktw.net", "https://bloom.bg", "https://foxs.pt", "https://yhoo.it", "https://kck.st",
        "https://flip.it", "https://ed.gr", "https://wh.gov", "https://go.nasa.gov", "https://1.usa.gov",
        "https://n.pr", "https://to.pbs.org", "https://nydn.us", "https://lat.ms", "https://usat.ly",
        "https://huff.to", "https://aje.io", "https://fxn.ws", "https://abcn.ws", "https://thebea.st",
        "https://ti.me", "https://rol.st", "https://elnuevodia.com", "https://on.today.com", "https://nbcnews.to",
        "https://nypost.com", "https://washingtonpost.com", "https://wsj.com", "https://bloomberg.com",
        "https://reuters.com", "https://apnews.com", "https://nytimes.com", "https://theguardian.com",
        "https://bbc.com", "https://cnn.com", "https://foxnews.com"
    }.OrderBy(s => s.Length)];

    private readonly List<string> _replacementEmails = [.. new List<string>
    {
        "a@b.co", "ab@c.co", "abc@d.co", "me@x.com", "you@y.net", "hi@go.org", "hey@now.io",
        "test@mail.com", "user@domain.net", "name@company.org", "person@website.io",
        "contact@business.com", "info@organization.net", "support@service.org",
        "hello@world.com", "greetings@earth.net", "welcome@home.org", "hi.there@place.io",
        "john.doe@example.com", "jane.smith@sample.net", "contact.us@bigcorp.org",
        "customer.service@shop.io", "info@longerdomainname.com", "support@techcompany.net",
        "sales@onlinebusiness.org", "marketing@brandname.io", "admin@systemname.com",
        "help@supportcenter.net", "careers@jobsite.org", "feedback@productname.io",
        "newsletter@mediaoutlet.com", "subscriptions@magazine.net", "donations@charity.org",
        "volunteers@community.io", "reservations@restaurant.com", "bookings@hotel.net",
        "tickets@event.org", "membership@club.io", "library@university.edu",
        "alumni@college.net", "patients@hospital.org", "appointments@clinic.io",
        "orders@onlinestore.com", "shipping@logistics.net", "returns@ecommerce.org",
        "accounting@finance.io", "payroll@hr.com", "recruiting@staffing.net",
        "training@education.org", "research@laboratory.io", "editor@newspaper.com",
        "classified@advert.net", "webmaster@website.org", "developer@software.io",
        "security@cyberfirm.com", "privacy@dataprotect.net", "legal@lawfirm.org",
        "compliance@regulatory.io", "investor.relations@corp.com", "media@pr.net",
        "press@publicity.org", "social.media@brand.io", "emergency@response.com",
        "maintenance@facility.net", "it.support@tech.org", "network.admin@systems.io"
    }.OrderBy(s => s.Length)];

    private readonly List<string> _replacementFirstNames = [.. new List<string>
    {
        "Aa", "Bo", "Cy", "Di", "Ed", "Fy", "Gy", "Hy", "Io", "Ja", "Ky", "Lu", "Mo", "Ny", "Ox", "Py",
        "Qu", "Ry", "Su", "Ty", "Ux", "Vy", "Wy", "Xy", "Yo", "Zy", "Ada", "Ben", "Cal", "Dex", "Eva",
        "Fay", "Gil", "Hal", "Ida", "Jay", "Kay", "Leo", "Mae", "Ned", "Ora", "Pia", "Rex", "Sky", "Tia",
        "Uma", "Val", "Wes", "Xena", "Yara", "Zane", "Abby", "Bart", "Cora", "Dale", "Elsa", "Fred",
        "Gina", "Hugo", "Iris", "Jack", "Kate", "Liam", "Mila", "Noah", "Olga", "Paul", "Quin", "Rose",
        "Sean", "Tara", "Ursa", "Vera", "Wade", "Xander", "Yolanda", "Zachary", "Abraham", "Beatrice",
        "Caroline", "Dominic", "Eleanor", "Franklin", "Gabrielle", "Harrison", "Isabella", "Jonathan",
        "Katherine", "Leonardo", "Margaret", "Nathaniel", "Olivia", "Penelope", "Quentin", "Rebecca",
        "Sebastian", "Theodore", "Ursula", "Victoria", "William", "Xavier", "Yvette", "Zoe"
    }.OrderBy(s => s.Length)];

    private readonly List<string> _replacementLastNames = [.. new List<string>
    {
        "Ax", "By", "Cu", "Do", "Ey", "Fo", "Gu", "Hy", "Ix", "Ju", "Ky", "Lo", "Mu", "Nu", "Ox", "Py",
        "Qu", "Ry", "Su", "Ty", "Ux", "Vy", "Wu", "Xu", "Yu", "Zu", "Ash", "Bly", "Cox", "Day", "Ely",
        "Fox", "Gay", "Hoy", "Ivy", "Jay", "Kay", "Lee", "May", "Noe", "Orr", "Poe", "Ray", "Shy", "Tay",
        "Udy", "Vey", "Way", "Yee", "Zoe", "Abel", "Bell", "Cole", "Duff", "Earl", "Finn", "Gray",
        "Hall", "Ivey", "Jett", "King", "Lane", "Moon", "Nash", "Owen", "Page", "Quinn", "Ross", "Shaw",
        "Tran", "Vang", "Ward", "Yang", "Zhou", "Adams", "Brown", "Clark", "Davis", "Evans", "Flynn",
        "Green", "Hayes", "Irwin", "Jones", "Klein", "Lewis", "Moore", "Nixon", "Olsen", "Payne",
        "Quinn", "Reyes", "Smith", "Taylor", "Uddin", "Vance", "White", "Yates", "Zhang", "Anderson",
        "Blackburn", "Campbell", "Donovan", "Edwards", "Ferguson", "Gonzalez", "Hamilton", "Ingram",
        "Johnson", "Kennedy", "Lawson", "Mitchell", "Nicholson", "O'Connor", "Patterson", "Quigley",
        "Robertson", "Stevenson", "Thompson", "Underwood", "Valentine", "Williamson", "Xavier", "Zimmerman"
    }.OrderBy(s => s.Length)];

    private readonly List<string> _replacementStrings = [.. new List<string>
    {
        "AA", "BB", "CC", "DD", "EE", "FF", "GG", "HH", "II", "JJ", "KK", "LL", "MM", "NN", "OO", "PP",
        "QQ", "RR", "SS", "TT", "UU", "VV", "WW", "XX", "YY", "ZZ", "Ace", "Biz", "Cap", "Dex", "Eco",
        "Flo", "Geo", "Hub", "Inc", "Jet", "Key", "Lab", "Max", "Neo", "Opt", "Pro", "Quo", "Rev", "Sys",
        "Tek", "Uni", "Vox", "Web", "Xcel", "Yotta", "Zoom", "Alpha", "Beta", "Cyber", "Delta", "Echo",
        "Focal", "Gamma", "Helix", "Ionic", "Jolta", "Kappa", "Logic", "Macro", "Nexus", "Omega", "Prime",
        "Quant", "Robo", "Sigma", "Terra", "Ultra", "Vecto", "Warp", "Xeno", "Yotta", "Zeta", "Acumen",
        "Bravo", "Centric", "Dynamo", "Epsilon", "Fusion", "Gravity", "Horizon", "Impulse", "Kinetic",
        "Lambda", "Magnum", "Neutron", "Omicron", "Photon", "Quantum", "Radiant", "Synergy", "Thermal",
        "Upsilon", "Vortex", "Wisdom", "Xenon", "Yield", "Zephyr", "Accelerate", "Benchmark", "Capitalize",
        "Dimension", "Enterprise", "Formulate", "Generator", "Harmonize", "Innovate", "Juxtapose",
        "Knowledge", "Leverage", "Magnitude", "Navigate", "Optimize", "Paradigm", "Quantify", "Resonate",
        "Stimulate", "Transform", "Ultimate", "Validate", "Wavelength", "Xenogenesis", "Yield", "Zenith",
        "Abundance", "Brilliance", "Confluence", "Divergence", "Emergence", "Fluency", "Governance",
        "Hierarchy", "Inference", "Juxtaposition", "Kinesthetics", "Luminescence", "Metamorphosis",
        "Nomenclature", "Oscillation", "Proliferation", "Quintessence", "Resilience", "Sustainability",
        "Transcendence", "Ubiquity", "Valence", "Wavelength", "Xenograft", "Yieldcurve", "Zeitgeist",
        "Accelerator", "Biometrics", "Cryptography", "Diagnostics", "Equilibrium", "Fabrication",
        "Granularity", "Holographic", "Integration", "Kinematics", "Lithography", "Multivariate",
        "Nanomedicine", "Optimization", "Parallelism", "Quantization", "Recombinant", "Stochastic",
        "Thermodynamic", "Ubiquitous", "Virtualization", "Waveguide", "Xenotransplant", "Yoctosecond",
        "Zettabyte"
    }.OrderBy(s => s.Length)];

    public string GetReplacementString(string originalString, NamedEntityType entityType)
    {
        return entityType switch
        {
            NamedEntityType.Person => GetPersonReplacement(originalString),
            NamedEntityType.Email => GetReplacement(originalString, _replacementEmails),
            NamedEntityType.Country => GetReplacement(originalString, _replacementCountries),
            NamedEntityType.Url => GetReplacement(originalString, _replacementUrls),
            NamedEntityType.Fund => GetReplacement(originalString, _replacementFunds),
            _ => GetReplacement(originalString, _replacementStrings),
        };
    }

    private static string GetReplacement(string original, List<string> sourceList)
    {
        int originalLength = original.Length;
        int hash = GetDeterministicHash(original);
        int allowedLengthDeviation = originalLength < 10
            ? 0
            : originalLength < 14
                ? 1
                : 2;

        var suitableReplacements = sourceList
            .Where(s => Math.Abs(s.Length - originalLength) <= allowedLengthDeviation)
            .ToList();

        if (suitableReplacements.Count == 0)
        {
            return ShuffleString(original);
        }

        int index = Math.Abs(hash) % suitableReplacements.Count;

        return suitableReplacements[index];
    }

    private string GetPersonReplacement(string original)
    {
        string[] parts = original.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return GetReplacement(original, _replacementLastNames);
        }
        else
        {
            string firstName = GetReplacement(parts[0], _replacementFirstNames);
            string lastName = GetReplacement(parts[^1], _replacementLastNames);
            return $"{firstName} {lastName}";
        }
    }

    private static int GetDeterministicHash(string input)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = SHA256.HashData(inputBytes);

        return BitConverter.ToInt32(hashBytes, 0);
    }

    private static string ShuffleString(string input)
    {
        char[] characters = input.ToCharArray();
        int n = characters.Length;

        while (n > 1)
        {
            n--;
            int k = Math.Abs(GetDeterministicHash(input + n.ToString()) % (n + 1));
            (characters[n], characters[k]) = (characters[k], characters[n]);
        }

        return new string(characters);
    }
}
