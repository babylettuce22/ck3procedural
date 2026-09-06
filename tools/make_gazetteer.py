"""
Builds assets/realworld_places.txt: every real-world place name a generated title must not land on.

The bulk comes from the vanilla CK3 English localisation — every barony, county, duchy, kingdom
and empire on the 867/1066 map, their adjectives, the cultural title names, the geographical
regions, the culture names and the holy sites. That is the set a CK3 player recognises on sight,
which is what makes "Yemen" jump off a generated map. A hand-kept supplement below covers the
world CK3's map stops short of: the Americas, East Asia, Oceania, and the modern names of places
the game only knows by their medieval ones.

Run from the repo root:  python tools/make_gazetteer.py [path-to-ck3/game]
The output is committed; the generator reads it as an embedded resource (MapGen/Gazetteer.cs),
so it never needs the game install at runtime.
"""
import re
import sys
from pathlib import Path

DEFAULT_GAME = Path(r"C:/Program Files (x86)/Steam/steamapps/common/Crusader Kings III/game")
OUT = Path(__file__).resolve().parent.parent / "assets" / "realworld_places.txt"

LINE = re.compile(r'^\s*([A-Za-z0-9_.\-]+):\d*\s*"(.*)"\s*(#.*)?$')

# (relative file, key filter) — the filter says which keys of that file are names.
SOURCES = [
    ("localization/english/titles_l_english.yml", re.compile(r"^[bcdkeh]_")),
    ("localization/english/titles_cultural_names_l_english.yml", re.compile(r".")),
    ("localization/english/regions_l_english.yml", re.compile(r".")),
    ("localization/english/culture/cultures_l_english.yml", re.compile(r".")),
    ("localization/english/religion/holy_sites_l_english.yml", re.compile(r".")),
]

# Places outside, or later than, the CK3 map. Single tokens only: a generated name has no spaces,
# so "New York" could never match anyway. Modern country names are here even where the game has
# the medieval one, because "Turkey" reads as real just as "Rum" does not.
SUPPLEMENT = """
Afghanistan Albania Algeria Andorra Angola Argentina Armenia Australia Austria Azerbaijan
Bahamas Bahrain Bangladesh Barbados Belarus Belgium Belize Benin Bhutan Bolivia Bosnia Botswana
Brazil Brunei Bulgaria Burma Burundi Cambodia Cameroon Canada Chad Chile China Colombia Comoros
Congo Croatia Cuba Cyprus Czechia Denmark Djibouti Dominica Ecuador Egypt Eritrea Estonia
Eswatini Ethiopia Fiji Finland France Gabon Gambia Georgia Germany Ghana Greece Grenada Guatemala
Guinea Guyana Haiti Honduras Hungary Iceland India Indonesia Iran Iraq Ireland Israel Italy
Jamaica Japan Jordan Kazakhstan Kenya Kiribati Korea Kosovo Kuwait Kyrgyzstan Laos Latvia Lebanon
Lesotho Liberia Libya Liechtenstein Lithuania Luxembourg Madagascar Malawi Malaysia Maldives Mali
Malta Mauritania Mauritius Mexico Micronesia Moldova Monaco Mongolia Montenegro Morocco Mozambique
Myanmar Namibia Nauru Nepal Netherlands Nicaragua Niger Nigeria Norway Oman Pakistan Palau
Palestine Panama Paraguay Peru Philippines Poland Portugal Qatar Romania Russia Rwanda Samoa
Senegal Serbia Seychelles Singapore Slovakia Slovenia Somalia Spain Sudan Suriname Sweden
Switzerland Syria Taiwan Tajikistan Tanzania Thailand Tibet Togo Tonga Tunisia Turkey
Turkmenistan Tuvalu Uganda Ukraine Uruguay Uzbekistan Vanuatu Venezuela Vietnam Yemen Zambia
Zimbabwe England Scotland Wales Britain Cornwall Ulster Prussia Bavaria Saxony Hanover Silesia
Bohemia Moravia Galicia Catalonia Aragon Castile Navarre Andalusia Brittany Normandy Burgundy
Provence Savoy Lombardy Tuscany Sicily Sardinia Corsica Flanders Holland Zeeland Friesland
Jutland Scania Lapland Karelia Siberia Crimea Caucasus Anatolia Thrace Macedonia Epirus Attica
Crete Rhodes Cyprus Levant Arabia Persia Khorasan Punjab Bengal Kashmir Gujarat Deccan Ceylon
Malabar Sumatra Java Borneo Sulawesi Luzon Mindanao Timor Papua Guam Hawaii Tasmania Queensland
Victoria Ontario Quebec Alberta Manitoba Yukon Alaska Texas Florida California Oregon Nevada
Arizona Utah Colorado Kansas Nebraska Dakota Montana Wyoming Idaho Iowa Missouri Arkansas
Louisiana Mississippi Alabama Tennessee Kentucky Ohio Indiana Illinois Michigan Wisconsin
Minnesota Maine Vermont Massachusetts Connecticut Delaware Maryland Virginia Carolina Pennsylvania
Jersey Patagonia Amazonia Yucatan Oaxaca Sonora Chihuahua Jalisco Guerrero Veracruz Tabasco
Chiapas Antioquia Cusco Lima Quito Bogota Caracas Santiago Montevideo Asuncion Brasilia Rio
Bahia Pernambuco Minas Parana Havana Kingston Ottawa Toronto Montreal Vancouver Calgary Winnipeg
Halifax Boston Chicago Detroit Philadelphia Baltimore Washington Atlanta Miami Houston Dallas
Austin Denver Phoenix Seattle Portland Sydney Melbourne Brisbane Perth Adelaide Canberra Darwin
Auckland Wellington Christchurch Otago Tokyo Kyoto Osaka Nagoya Nara Edo Sapporo Hokkaido Honshu
Kyushu Shikoku Okinawa Seoul Busan Pyongyang Silla Goryeo Joseon Beijing Nanjing Shanghai
Guangzhou Canton Hangzhou Suzhou Chengdu Chongqing Wuhan Xian Luoyang Kaifeng Tianjin Harbin
Shenyang Manchuria Yunnan Sichuan Guangdong Fujian Zhejiang Jiangsu Shandong Hebei Henan Hubei
Hunan Shaanxi Shanxi Gansu Qinghai Xinjiang Hainan Macau Hongkong Hanoi Saigon Hue Angkor Bangkok
Ayutthaya Sukhothai Rangoon Mandalay Pagan Pegu Malacca Johor Penang Jakarta Batavia Surabaya
Bali Lombok Manila Cebu Kabul Kandahar Herat Balkh Tehran Isfahan Shiraz Tabriz Mashhad Baghdad
Basra Mosul Damascus Aleppo Beirut Jerusalem Mecca Medina Riyadh Jeddah Sanaa Aden Muscat Doha
Dubai Cairo Alexandria Luxor Aswan Khartoum Tripoli Tunis Algiers Rabat Fez Marrakesh Tangier
Casablanca Dakar Timbuktu Accra Lagos Kano Benin Douala Kinshasa Luanda Nairobi Mombasa
Zanzibar Kilwa Mogadishu Addis Axum Gondar Harar Lusaka Harare Maputo Pretoria Johannesburg
Capetown Durban Antananarivo Lisbon Porto Madrid Barcelona Seville Valencia Toledo Granada
Cordoba Bilbao Paris Lyon Marseille Bordeaux Toulouse Rouen Reims Orleans Nantes Strasbourg
London York Winchester Canterbury Oxford Cambridge Bristol Norwich Exeter Lincoln Chester Durham
Newcastle Edinburgh Glasgow Stirling Perth Aberdeen Inverness Dublin Cork Galway Limerick
Cardiff Swansea Amsterdam Rotterdam Utrecht Antwerp Bruges Ghent Brussels Liege Berlin Hamburg
Munich Cologne Frankfurt Mainz Trier Aachen Bremen Lubeck Leipzig Dresden Nuremberg Augsburg
Regensburg Vienna Salzburg Innsbruck Graz Zurich Bern Basel Geneva Prague Brno Krakow Warsaw
Gdansk Poznan Wroclaw Budapest Bratislava Belgrade Zagreb Sarajevo Sofia Bucharest Athens
Sparta Thebes Corinth Delphi Olympia Byzantium Constantinople Istanbul Ankara Smyrna Izmir Trabzon
Moscow Kiev Kyiv Novgorod Minsk Vilnius Riga Tallinn Helsinki Stockholm Uppsala Oslo Bergen
Trondheim Copenhagen Roskilde Reykjavik Rome Milan Venice Genoa Florence Pisa Siena Naples
Palermo Bari Ravenna Bologna Verona Padua Turin Trieste Malta Valletta Tehran Kabul Delhi Agra
Lahore Karachi Mumbai Bombay Calcutta Kolkata Madras Chennai Bangalore Hyderabad Goa Kandy
Colombo Dhaka Kathmandu Lhasa Thimphu Kashgar Samarkand Bukhara Khiva Merv Tashkent Almaty
Astana Bishkek Dushanbe Ashgabat Baku Tbilisi Yerevan Karakorum Ulaanbaatar Europe Asia Africa
America Antarctica Oceania Australasia Atlantic Pacific Arctic Mediterranean Baltic Adriatic
Aegean Caspian Sahara Sahel Himalaya Alps Andes Rockies Urals Carpathians Pyrenees Balkans
Scandinavia Iberia Britannia Hibernia Caledonia Gaul Germania Italia Hispania Dacia Illyria
Pannonia Moesia Cappadocia Galatia Lydia Phrygia Bithynia Pontus Armenia Mesopotamia Assyria
Babylon Babylonia Sumer Akkad Elam Media Parthia Bactria Sogdia Gandhara Magadha Maurya Gupta
Chola Pallava Chalukya Rashtrakuta Vijayanagara Delhi Sultanate Mughal Timurid Safavid Ottoman
Seljuk Ghaznavid Abbasid Umayyad Fatimid Ayyubid Mamluk Almohad Almoravid Aztec Maya Inca
Olmec Toltec Mixtec Zapotec Tenochtitlan Teotihuacan Cuzco Machu Picchu Cahokia Mississippian
Iroquois Cherokee Sioux Apache Navajo Comanche Cheyenne Mohawk Huron Algonquin Ojibwe Cree
Inuit Aleut Haida Tlingit Maori Aborigine Polynesia Melanesia Micronesia Tahiti Tonga Samoa
Fiji Zulu Xhosa Swazi Ashanti Yoruba Igbo Hausa Fulani Songhai Mali Ghana Kanem Bornu Kongo
Lunda Luba Buganda Ethiopia Abyssinia Nubia Kush Meroe Punt Carthage Numidia Mauretania
Cyrenaica Tripolitania Fezzan Tuareg Berber Egyptian Libyan Sudanese Somali Ethiopian Kenyan
Nigerian Ghanaian Malian Moroccan Algerian Tunisian Turkish Persian Iranian Iraqi Syrian
Lebanese Israeli Jordanian Saudi Yemeni Omani Kuwaiti Qatari Emirati Afghan Pakistani Indian
Bengali Nepali Bhutanese Burmese Thai Laotian Khmer Cambodian Vietnamese Malay Malaysian
Indonesian Filipino Chinese Japanese Korean Mongolian Tibetan Uyghur Kazakh Kyrgyz Uzbek Tajik
Turkmen Russian Ukrainian Belarusian Polish Czech Slovak Hungarian Romanian Bulgarian Serbian
Croatian Bosnian Slovene Albanian Greek Macedonian Italian Spanish Portuguese French German
Austrian Swiss Dutch Belgian Danish Swedish Norwegian Finnish Icelandic Irish Scottish Welsh
English British American Canadian Mexican Cuban Brazilian Argentine Chilean Peruvian Colombian
Venezuelan Australian Kiwi Hawaiian Alaskan Texan Californian Floridian Yankee
"""


def read_loc(path: Path, keys: re.Pattern) -> list[str]:
    names = []
    for raw in path.read_text(encoding="utf-8-sig").splitlines():
        m = LINE.match(raw)
        if not m or not keys.search(m.group(1)):
            continue
        value = m.group(2)
        if any(ch in value for ch in "$[]#|"):
            continue  # a reference to another key, a datafunction or a colour code — not a name
        names.append(value)
    return names


def usable(name: str) -> bool:
    name = name.strip()
    if len(name) < 3 or any(ch.isdigit() for ch in name):
        return False
    if " " in name or "'" in name or "(" in name:
        return False  # a generated name never has spaces or apostrophes
    return True


def main() -> None:
    game = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_GAME
    if not game.is_dir():
        sys.exit(f"CK3 game folder not found: {game}")

    seen: dict[str, str] = {}

    def add(name: str) -> None:
        name = name.strip()
        if usable(name):
            seen.setdefault(name.casefold(), name)

    for rel, keys in SOURCES:
        before = len(seen)
        for name in read_loc(game / rel, keys):
            add(name)
        print(f"{rel}: +{len(seen) - before}")

    before = len(seen)
    for name in SUPPLEMENT.split():
        add(name)
    print(f"supplement: +{len(seen) - before}")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    body = "\n".join(seen[k] for k in sorted(seen)) + "\n"
    OUT.write_text(body, encoding="utf-8", newline="\n")
    print(f"{len(seen)} names -> {OUT}")


if __name__ == "__main__":
    main()
