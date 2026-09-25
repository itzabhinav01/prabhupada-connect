using System;
using System.Collections.Generic;

namespace VedaBaseModern.Core.Metadata
{
    /// <summary>
    /// Canonical chapter titles for Bhagavad-gītā As It Is, Śrīmad-Bhāgavatam,
    /// and Śrī Caitanya-caritāmṛta, matching vedabase.io's published chapter
    /// naming. Sourced directly from each book's own embedded internal
    /// navigation labels in the source RTF exports (extracted via
    /// CorpusPipeline's `extract-titles` diagnostic command), not manually
    /// transcribed, so the wording matches this corpus's actual text exactly.
    ///
    /// A handful of chapters (SB 10.87, 11.28, 11.29) had no extractable
    /// label in the source RTF and are intentionally absent here; callers
    /// fall back to a bare "Chapter N" / "Canto C Chapter N" when a lookup
    /// misses.
    /// </summary>
    public static class CanonicalChapterTitles
    {
        public static readonly Dictionary<int, string> Bg = new()
        {
            { 1, "Observing the Armies on the Battlefield of Kurukṣetra" },
            { 2, "Contents of the Gītā Summarized" },
            { 3, "Karma-yoga" },
            { 4, "Transcendental Knowledge" },
            { 5, "Karma-yoga-Action in Kṛṣṇa Consciousness" },
            { 6, "Dhyāna-yoga" },
            { 7, "Knowledge of the Absolute" },
            { 8, "Attaining the Supreme" },
            { 9, "The Most Confidential Knowledge" },
            { 10, "The Opulence of the Absolute" },
            { 11, "The Universal Form" },
            { 12, "Devotional Service" },
            { 13, "Nature, the Enjoyer, and Consciousness" },
            { 14, "The Three Modes of Material Nature" },
            { 15, "The Yoga of the Supreme Person" },
            { 16, "The Divine and Demoniac Natures" },
            { 17, "The Divisions of Faith" },
            { 18, "Conclusion—The Perfection of Renunciation" }
        };

        // Front-matter sections that precede Chapter 1, in display order.
        public static readonly List<string> BgFrontMatter = new()
        {
            "Setting the Scene", "Dedication", "Preface", "Introduction"
        };

        public static readonly List<string> SbFrontMatter = new()
        {
            "Dedication", "Preface", "Introduction"
        };

        public static readonly List<string> CcAdiFrontMatter = new()
        {
            "Dedication", "Introduction", "Preface", "Foreword"
        };

        // Keyed by (Canto, Chapter).
        public static readonly Dictionary<(int Canto, int Chapter), string> Sb = new()
        {
            { (1,1), "Questions by the Sages" }, { (1,2), "Divinity and Divine Service" },
            { (1,3), "Kṛṣṇa Is the Source of All Incarnations" }, { (1,4), "The Appearance of Śrī Nārada" },
            { (1,5), "Nārada's Instructions on Śrīmad-Bhāgavatam for Vyāsadeva" }, { (1,6), "Conversation Between Nārada and Vyāsadeva" },
            { (1,7), "The Son of Droṇa Punished" }, { (1,8), "Prayers by Queen Kuntī, and Parīkṣit Saved" },
            { (1,9), "The Passing Away of Bhīṣmadeva in the Presence of Lord Kṛṣṇa" }, { (1,10), "Departure of Lord Kṛṣṇa for Dvārakā" },
            { (1,11), "Lord Kṛṣṇa's Entrance into Dvārakā" }, { (1,12), "Birth of Emperor Parīkṣit" },
            { (1,13), "Dhṛtarāṣṭra Quits Home" }, { (1,14), "The Disappearance of Lord Kṛṣṇa" },
            { (1,15), "The Pāṇḍavas Retire Timely" }, { (1,16), "How Parīkṣit Received the Age of Kali" },
            { (1,17), "Punishment and Reward of Kali" }, { (1,18), "Mahārāja Parīkṣit Cursed by a Brāhmaṇa Boy" },
            { (1,19), "The Appearance of Śukadeva Gosvāmī" },

            { (2,1), "The First Step in God Realization" }, { (2,2), "The Lord in the Heart" },
            { (2,3), "Pure Devotional Service: The Change in Heart" }, { (2,4), "The Process of Creation" },
            { (2,5), "The Cause of All Causes" }, { (2,6), "Puruṣa-sūkta Confirmed" },
            { (2,7), "Scheduled Incarnations with Specific Functions" }, { (2,8), "Questions by King Parīkṣit" },
            { (2,9), "Answers by Citing the Lord's Version" }, { (2,10), "Bhāgavatam Is the Answer to All Questions" },

            { (3,1), "Questions by Vidura" }, { (3,2), "Remembrance of Lord Kṛṣṇa" },
            { (3,3), "The Lord's Pastimes Out of Vṛndāvana" }, { (3,4), "Vidura Approaches Maitreya" },
            { (3,5), "Vidura's Talks with Maitreya" }, { (3,6), "Creation of the Universal Form" },
            { (3,7), "Further Inquires by Vidura" }, { (3,8), "Manifestation of Brahmā from Garbhodakaśāyī Viṣṇu" },
            { (3,9), "Brahmā's Prayers for Creative Energy" }, { (3,10), "Divisions of the Creation" },
            { (3,11), "Calculation of Time, from the Atom" }, { (3,12), "Creation of the Kumāras and Others" },
            { (3,13), "The Appearance of Lord Varāha" }, { (3,14), "Pregnancy of Diti in the Evening" },
            { (3,15), "Description of the kingdom of God" }, { (3,16), "The Two Doorkeepers of Vaikuṇṭha, Jaya and Vijaya, Cursed by the Sages" },
            { (3,17), "Victory of Hiraṇyākṣa Over All the Directions of the Universe" }, { (3,18), "The Battle Between Lord Boar and the Demon Hiraṇyākṣa" },
            { (3,19), "The Killing of the Demon Hiraṇyākṣa" }, { (3,20), "Conversation Between Maitreya and Vidura" },
            { (3,21), "Conversation Between Manu and Kardama" }, { (3,22), "The Marriage of Kardama Muni and Devahūti" },
            { (3,23), "Devahūti's Lamentation" }, { (3,24), "The Renunciation of Kardama Muni" },
            { (3,25), "The Glories of Devotional Service" }, { (3,26), "Fundamental Principles of Material Nature" },
            { (3,27), "Understanding Material Nature" }, { (3,28), "Kapila's Instructions on the Execution of Devotional Service" },
            { (3,29), "Explanation of Devotional Service by Lord Kapila" }, { (3,30), "Description by Lord Kapila of Adverse Fruitive Activities" },
            { (3,31), "Lord Kapila's Instructions on the Movements of the Living Entities" }, { (3,32), "Entanglement in Fruitive Activities" },
            { (3,33), "Activities of Kapila" },

            { (4,1), "Genealogical Table of the Daughters of Manu" }, { (4,2), "Dakṣa Curses Lord Śiva" },
            { (4,3), "Talks Between Lord Śiva and Satī" }, { (4,4), "Satī Quits Her Body" },
            { (4,5), "Frustration of the Sacrifice of Dakṣa" }, { (4,6), "Brahmā Satisfies Lord Śiva" },
            { (4,7), "The Sacrifice Performed by Dakṣa" }, { (4,8), "Dhruva Mahārāja Leaves Home for the Forest" },
            { (4,9), "Dhruva Mahārāja Returns Home" }, { (4,10), "Dhruva Mahārāja's Fight With the Yakṣas" },
            { (4,11), "Svāyambhuva Manu Advises Dhruva Mahārāja to Stop Fighting" }, { (4,12), "Dhruva Mahārāja Goes Back to Godhead" },
            { (4,13), "Description of the Descendants of Dhruva Mahārāja" }, { (4,14), "The Story of King Vena" },
            { (4,15), "King Pṛthu's Appearance and Coronation" }, { (4,16), "Praise of King Pṛthu by the Professional Reciters" },
            { (4,17), "Mahārāja Pṛthu Becomes Angry at the Earth" }, { (4,18), "Pṛthu Mahārāja Milks the Earth Planet" },
            { (4,19), "King Pṛthu's One Hundred Horse Sacrifices" }, { (4,20), "Lord Viṣṇu's Appearance in the Sacrificial Arena of Mahārāja Pṛthu" },
            { (4,21), "Instructions by Mahārāja Pṛthu" }, { (4,22), "Pṛthu Mahārāja's Meeting with the Four Kumāras" },
            { (4,23), "Mahārāja Pṛthu's Going Back Home" }, { (4,24), "Chanting the Song Sung by Lord Śiva" },
            { (4,25), "The Descriptions of the Characteristics of King Purañjana" }, { (4,26), "King Purañjana Goes to the Forest to Hunt, and His Queen Becomes Angry" },
            { (4,27), "Attack by Caṇḍavega on the City of King Purañjana: the Character of Kālakanyā" }, { (4,28), "Purañjana Becomes a Woman in the Next Life" },
            { (4,29), "Talks Between Nārada and King Prācīnabarhi" }, { (4,30), "The Activities of the Pracetās" },
            { (4,31), "Nārada Instructs the Pracetās" },

            { (5,1), "The Activities of Mahārāja Priyavrata" }, { (5,2), "The Activities of Mahārāja Āgnīdhra" },
            { (5,3), "Ṛṣabhadeva's Appearance in the Womb of Merudevī, the Wife of King Nābhi" }, { (5,4), "The Characteristics of Ṛṣabhadeva, the Supreme Personality of Godhead" },
            { (5,5), "Lord Ṛṣabhadeva's Teachings to His Sons" }, { (5,6), "The Activities of Lord Ṛṣabhadeva" },
            { (5,7), "The Activities of King Bharata" }, { (5,8), "A Description of the Character of Bharata Mahārāja" },
            { (5,9), "The Supreme Character of Jaḍa Bharata" }, { (5,10), "The Discussion Between Jaḍa Bharata and Mahārāja Rahūgaṇa" },
            { (5,11), "Jaḍa Bharata Instructs King Rahūgaṇa" }, { (5,12), "Conversation Between Mahārāja Rahūgaṇa and Jaḍa Bharata" },
            { (5,13), "Further Talks Between King Rahūgaṇa and Jaḍa Bharata" }, { (5,14), "The Material World as the Great Forest of Enjoyment" },
            { (5,15), "The Glories of the Descendants of King Priyavrata" }, { (5,16), "A Description of Jambūdvīpa" },
            { (5,17), "The Descent of the River Ganges" }, { (5,18), "The Prayers Offered to the Lord by the Residents of Jambūdvīpa" },
            { (5,19), "A Description of the Island of Jambūdvīpa" }, { (5,20), "Studying the Structure of the Universe" },
            { (5,21), "The Movements of the Sun" }, { (5,22), "The Orbits of the Planets" },
            { (5,23), "The Śiśumāra Planetary Systems" }, { (5,24), "The Subterranean Heavenly Planets" },
            { (5,25), "The Glories of Lord Ananta" }, { (5,26), "A Description of the Hellish Planets" },

            { (6,1), "The History of the Life of Ajāmila" }, { (6,2), "Ajāmila Delivered by the Viṣṇudūtas" },
            { (6,3), "Yamarāja Instructs His Messengers" }, { (6,4), "The Haṁsa-guhya Prayers" },
            { (6,5), "Nārada Muni Cursed by Prajāpati Dakṣa" }, { (6,6), "The Progeny of the Daughters of Dakṣa" },
            { (6,7), "Indra Offends His Spiritual Master, Bṛhaspati" }, { (6,8), "The Nārāyaṇa-kavaca Shield" },
            { (6,9), "Appearance of the Demon Vṛtrāsura" }, { (6,10), "The Battle Between the Demigods and Vṛtrāsura" },
            { (6,11), "The Transcendental Qualities of Vṛtrāsura" }, { (6,12), "Vṛtrāsura's Glorious Death" },
            { (6,13), "King Indra Afflicted by Sinful Reaction" }, { (6,14), "King Citraketu's Lamentation" },
            { (6,15), "The Saints Nārada and Aṅgirā Instruct King Citraketu" }, { (6,16), "King Citraketu Meets the Supreme Lord" },
            { (6,17), "Mother Pārvatī Curses Citraketu" }, { (6,18), "Diti Vows to Kill King Indra" },
            { (6,19), "Performing the Puṁsavana Ritualistic Ceremony" },

            { (7,1), "The Supreme Lord Is Equal to Everyone" }, { (7,2), "Hiraṇyakaśipu, King of the Demons" },
            { (7,3), "Hiraṇyakaśipu's Plan to Become Immortal" }, { (7,4), "Hiraṇyakaśipu Terrorizes the Universe" },
            { (7,5), "Prahlāda Mahārāja, the Saintly Son of Hiraṇyakaśipu" }, { (7,6), "Prahlāda Instructs His Demoniac Schoolmates" },
            { (7,7), "What Prahlāda Learned in the Womb" }, { (7,8), "Lord Nṛsiṁhadeva Slays the king of the Demons" },
            { (7,9), "Prahlāda Pacifies Lord Nṛsiṁhadeva with Prayers" }, { (7,10), "Prahlāda, the Best Among Exalted Devotees" },
            { (7,11), "The Perfect Society: Four Social Classes" }, { (7,12), "The Perfect Society: Four Spiritual Classes" },
            { (7,13), "The Behavior of a Perfect Person" }, { (7,14), "Ideal Family Life" },
            { (7,15), "Instructions for Civilized Human Beings" },

            { (8,1), "The Manus, Administrators of the Universe" }, { (8,2), "The Elephant Gajendra's Crisis" },
            { (8,3), "Gajendra's Prayers of Surrender" }, { (8,4), "Gajendra Returns to the Spiritual World" },
            { (8,5), "The Demigods Appeal to the Lord for Protection" }, { (8,6), "The Demigods and Demons Declare a Truce" },
            { (8,7), "Lord Śiva Saves the Universe by Drinking Poison" }, { (8,8), "The Churning of the Milk Ocean" },
            { (8,9), "The Lord Incarnates as Mohinī-Mūrti" }, { (8,10), "The Battle Between the Demigods and the Demons" },
            { (8,11), "King Indra Annihilates the Demons" }, { (8,12), "The Mohinī-mūrti Incarnation Bewilders Lord Śiva" },
            { (8,13), "Description of Future Manus" }, { (8,14), "The System of Universal Management" },
            { (8,15), "Bali Mahārāja Conquers the Heavenly Planets" }, { (8,16), "Executing the Payo-vrata Process of Worship" },
            { (8,17), "The Supreme Lord Agrees to Become Aditi's Son" }, { (8,18), "Lord Vāmanadeva, the Dwarf Incarnation" },
            { (8,19), "Lord Vāmanadeva Begs Charity from Bali Mahārāja" }, { (8,20), "Bali Mahārāja Surrenders the Universe" },
            { (8,21), "Bali Mahārāja Arrested by the Lord" }, { (8,22), "Bali Mahārāja Surrenders His Life" },
            { (8,23), "The Demigods Regain the Heavenly Planets" }, { (8,24), "Matsya, the Lord's Fish Incarnation" },

            { (9,1), "King Sudyumna Becomes a Woman" }, { (9,2), "The Dynasties of the Sons of Manu" },
            { (9,3), "The Marriage of Sukanyā and Cyavana Muni" }, { (9,4), "Ambarīṣa Mahārāja Offended by Durvāsā Muni" },
            { (9,5), "Durvāsā Muni's Life Spared" }, { (9,6), "The Downfall of Saubhari Muni" },
            { (9,7), "The Descendants of King Māndhātā" }, { (9,8), "The Sons of Sagara Meet Lord Kapiladeva" },
            { (9,9), "The Dynasty of Aṁśumān" }, { (9,10), "The Pastimes of the Supreme Lord, Rāmacandra" },
            { (9,11), "Lord Rāmacandra Rules the World" }, { (9,12), "The Dynasty of Kuśa, the Son of Lord Rāmacandra" },
            { (9,13), "The Dynasty of Mahārāja Nimi" }, { (9,14), "King Purūravā Enchanted by Urvaśī" },
            { (9,15), "Paraśurāma, the Lord's Warrior Incarnation" }, { (9,16), "Lord Paraśurāma Destroys the World's Ruling Class" },
            { (9,17), "The Dynasties of the Sons of Purūravā" }, { (9,18), "King Yayāti Regains His Youth" },
            { (9,19), "King Yayāti Achieves Liberation" }, { (9,20), "The Dynasty of Pūru" },
            { (9,21), "The Dynasty of Bharata" }, { (9,22), "The Descendants of Ajamīḍha" },
            { (9,23), "The Dynasties of the Sons of Yayāti" }, { (9,24), "Kṛṣṇa the Supreme Personality of Godhead" },

            { (10,1), "The Advent of Lord Kṛṣṇa: Introduction" }, { (10,2), "Prayers by the Demigods for Lord Kṛṣṇa in the Womb" },
            { (10,3), "The Birth of Lord Kṛṣṇa" }, { (10,4), "The Atrocities of King Kaṁsa" },
            { (10,5), "The Meeting of Nanda Mahārāja and Vasudeva" }, { (10,6), "The Killing of the Demon Pūtanā" },
            { (10,7), "The Killing of the Demon Tṛṇāvarta" }, { (10,8), "Lord Kṛṣṇa Shows the Universal Form Within His Mouth" },
            { (10,9), "Mother Yaśodā Binds Lord Kṛṣṇa" }, { (10,10), "Deliverance of the Yamala-arjuna Trees" },
            { (10,11), "The Childhood Pastimes of Kṛṣṇa" }, { (10,12), "The Killing of the Demon Aghāsura" },
            { (10,13), "The Stealing of the Boys and Calves by Brahmā" }, { (10,14), "Brahmā's Prayers to Lord Kṛṣṇa" },
            { (10,15), "The Killing of Dhenuka, the Ass Demon" }, { (10,16), "Kṛṣṇa Chastises the Serpent Kāliya" },
            { (10,17), "The History of Kāliya" }, { (10,18), "Lord Balarāma Slays the Demon Pralamba" },
            { (10,19), "Swallowing the Forest Fire" }, { (10,20), "The Rainy Season and Autumn in Vṛndāvana" },
            { (10,21), "The Gopīs Glorify the Song of Kṛṣṇa's Flute" }, { (10,22), "Kṛṣṇa Steals the Garments of the Unmarried Gopīs" },
            { (10,23), "The Brāhmaṇas' Wives Blessed" }, { (10,24), "Worshiping Govardhana Hill" },
            { (10,25), "Lord Kṛṣṇa Lifts Govardhana Hill" }, { (10,26), "Wonderful Kṛṣṇa" },
            { (10,27), "Lord Indra and Mother Surabhi Offer Prayers" }, { (10,28), "Kṛṣṇa Rescues Nanda Mahārāja from the Abode of Varuṇa" },
            { (10,29), "Kṛṣṇa and the Gopīs Meet for the Rāsa Dance" }, { (10,30), "The Gopīs Search for Kṛṣṇa" },
            { (10,31), "The Gopīs' Songs of Separation" }, { (10,32), "The Reunion" },
            { (10,33), "The Rāsa Dance" }, { (10,34), "Nanda Mahārāja Saved and Śaṅkhacūḍa Slain" },
            { (10,35), "The Gopīs Sing of Kṛṣṇa as He Wanders in the Forest" }, { (10,36), "The Slaying of Ariṣṭā, the Bull Demon" },
            { (10,37), "The Killing of the Demons Keśi and Vyoma" }, { (10,38), "Akrūra's Arrival in Vṛndāvana" },
            { (10,39), "Akrūra's Vision" }, { (10,40), "The Prayers of Akrūra" },
            { (10,41), "Kṛṣṇa and Balarāma Enter Mathurā" }, { (10,42), "The Breaking of the Sacrificial Bow" },
            { (10,43), "Kṛṣṇa Kills the Elephant Kuvalayāpīḍa" }, { (10,44), "The Killing of Kaṁsa" },
            { (10,45), "Kṛṣṇa Rescues His Teacher's Son" }, { (10,46), "Uddhava Visits Vṛndāvana" },
            { (10,47), "The Song of the Bee" }, { (10,48), "Kṛṣṇa Pleases His Devotees" },
            { (10,49), "Akrūra's Mission in Hastināpura" }, { (10,50), "Kṛṣṇa Establishes the City of Dvārakā" },
            { (10,51), "The Deliverance of Mucukunda" }, { (10,52), "Rukmiṇī's Message to Lord Kṛṣṇa" },
            { (10,53), "Kṛṣṇa Kidnaps Rukmiṇī" }, { (10,54), "The Marriage of Kṛṣṇa and Rukmiṇī" },
            { (10,55), "The History of Pradyumna" }, { (10,56), "The Syamantaka Jewel" },
            { (10,57), "Satrājit Murdered, the Jewel Returned" }, { (10,58), "Kṛṣṇa Marries Five Princesses" },
            { (10,59), "The Killing of the Demon Naraka" }, { (10,60), "Lord Kṛṣṇa Teases Queen Rukmiṇī" },
            { (10,61), "Lord Balarāma Slays Rukmī" }, { (10,62), "The Meeting of Ūṣā and Aniruddha" },
            { (10,63), "Lord Kṛṣṇa Fights with Bāṇāsura" }, { (10,64), "The Deliverance of King Nṛga" },
            { (10,65), "Lord Balarāma Visits Vṛndāvana" }, { (10,66), "Pauṇḍraka, the False Vāsudeva" },
            { (10,67), "Lord Balarāma Slays Dvivida Gorilla" }, { (10,68), "The Marriage of Sāmba" },
            { (10,69), "Nārada Muni Visits Lord Kṛṣṇa's Palaces in Dvārakā" }, { (10,70), "Lord Kṛṣṇa's Daily Activities" },
            { (10,71), "The Lord Travels to Indraprastha" }, { (10,72), "The Slaying of the Demon Jarāsandha" },
            { (10,73), "Lord Kṛṣṇa Blesses the Liberated Kings" }, { (10,74), "The Deliverance of Śiśupāla at the Rājasūya Sacrifice" },
            { (10,75), "Duryodhana Humiliated" }, { (10,76), "The Battle Between Śālva and the Vṛṣṇis" },
            { (10,77), "Lord Kṛṣṇa Slays the Demon Śālva" }, { (10,78), "The Killing of Dantavakra, Vidūratha and Romaharṣaṇa" },
            { (10,79), "Lord Balarāma Goes on Pilgrimage" }, { (10,80), "The Brāhmaṇa Sudāmā Visits Lord Kṛṣṇa in Dvārakā" },
            { (10,81), "The Lord Blesses Sudāmā Brāhmaṇa" }, { (10,82), "Kṛṣṇa and Balarāma Meet the Inhabitants of Vṛndāvana" },
            { (10,83), "Draupadī Meets the Queens of Kṛṣṇa" }, { (10,84), "The Sages' Teachings at Kurukṣetra" },
            { (10,85), "Lord Kṛṣṇa Instructs Vasudeva and Retrieves Devakī's Sons" }, { (10,86), "Arjuna Kidnaps Subhadrā, and Kṛṣṇa Blesses His Devotees" },
            { (10,88), "Lord Śiva Saved from Vṛkāsura" }, { (10,89), "Kṛṣṇa and Arjuna Retrieve a Brāhmaṇa's Sons" },
            { (10,90), "Summary of Lord Kṛṣṇa's Glories" },

            { (11,1), "The Curse Upon the Yadu Dynasty" }, { (11,2), "Mahārāja Nimi Meets the Nine Yogendras" },
            { (11,3), "Liberation from the Illusory Energy" }, { (11,4), "Drumila Explains the Incarnations of Godhead to King Nimi" },
            { (11,5), "Nārada Concludes His Teachings to Vasudeva" }, { (11,6), "The Yadu Dynasty Retires to Prabhāsa" },
            { (11,7), "Lord Kṛṣṇa Instructs Uddhava" }, { (11,8), "The Story of Piṅgalā" },
            { (11,9), "Detachment from All that Is Material" }, { (11,10), "The Nature of Fruitive Activity" },
            { (11,11), "The Symptoms of Conditioned and Liberated Living Entities" }, { (11,12), "Beyond Renunciation and Knowledge" },
            { (11,13), "The Haṁsa-avatāra Answers the Questions of the Sons of Brahmā" }, { (11,14), "Lord Kṛṣṇa Explains the Yoga System to Śrī Uddhava" },
            { (11,15), "Lord Kṛṣṇa's Description of Mystic Yoga Perfections" }, { (11,16), "The Lord's Opulence" },
            { (11,17), "Lord Kṛṣṇa's Description of the Varṇāśrama System" }, { (11,18), "Description of Varṇāśrama-dharma" },
            { (11,19), "The Perfection of Spiritual Knowledge" }, { (11,20), "Pure Devotional Service Surpasses Knowledge and Detachment" },
            { (11,21), "Lord Kṛṣṇa's Explanation of the Vedic Path" }, { (11,22), "Enumeration of the Elements of Material Creation" },
            { (11,23), "The Song of the Avantī Brāhmaṇa" }, { (11,24), "The Philosophy of Sāṅkhya" },
            { (11,25), "The Three Modes of Nature and Beyond" }, { (11,26), "The Aila-gītā" },
            { (11,27), "Lord Kṛṣṇa's Instructions on the Process of Deity Worship" },
            { (11,30), "The Disappearance of the Yadu Dynasty" }, { (11,31), "The Disappearance of Lord Śrī Kṛṣṇa" },

            { (12,1), "The Degraded Dynasties of Kali-yuga" }, { (12,2), "The Symptoms of Kali-yuga" },
            { (12,3), "The Bhūmi-gītā" }, { (12,4), "The Four Categories of Universal Annihilation" },
            { (12,5), "Śukadeva Gosvāmī's Final Instructions to Mahārāja Parīkṣit" }, { (12,6), "Mahārāja Parīkṣit Passes Away" },
            { (12,7), "The Purāṇic Literatures" }, { (12,8), "Mārkaṇḍeya's Prayers to Nara-Nārāyaṇa Ṛṣi" },
            { (12,9), "Mārkaṇḍeya Ṛṣi Sees the Illusory Potency of the Lord" }, { (12,10), "Lord Śiva and Umā Glorify Mārkaṇḍeya Ṛṣi" },
            { (12,11), "Summary Description of the Mahāpuruṣa" }, { (12,12), "The Topics of Śrīmad-Bhāgavatam Summarized" },
            { (12,13), "The Glories of Śrīmad-Bhāgavatam" }
        };

        public static readonly Dictionary<int, string> CcAdi = new()
        {
            { 1, "The Spiritual Masters" },
            { 2, "Śrī Caitanya Mahāprabhu Is the Supreme Personality of Godhead" },
            { 3, "The External Reasons for the Appearance of Śrī Caitanya Mahāprabhu" },
            { 4, "The Confidential Reasons for the Appearance of Śrī Caitanya Mahāprabhu" },
            { 5, "The Glories of Lord Nityānanda Balarāma" },
            { 6, "The Glories of Śrī Advaita Ācārya" },
            { 7, "Lord Caitanya in Five Features" },
            { 8, "The Author Receives the Orders of Kṛṣṇa and Guru" },
            { 9, "The Desire Tree of Devotional Service" },
            { 10, "The Trunk, Branches and Subbranches of the Caitanya Tree" },
            { 11, "The Expansions of Lord Nityānanda" },
            { 12, "The Expansions of Advaita Ācārya and Gadādhara Paṇḍita" },
            { 13, "The Advent of Lord Śrī Caitanya Mahāprabhu" },
            { 14, "Lord Caitanya's Childhood Pastimes" },
            { 15, "The Lord's Paugaṇḍa-līlā" },
            { 16, "The Pastimes of the Lord in His Childhood and Youth" },
            { 17, "The Pastimes of Lord Caitanya Mahāprabhu in His Youth" }
        };

        public static readonly Dictionary<int, string> CcMadhya = new()
        {
            { 1, "The Later Pastimes of Lord Śrī Caitanya Mahāprabhu" },
            { 2, "The Ecstatic Manifestations of Lord Śrī Caitanya Mahāprabhu" },
            { 3, "Lord Śrī Caitanya Mahāprabhu's Stay at the House of Advaita Ācārya" },
            { 4, "Śrī Mādhavendra Puri's Devotional Service" },
            { 5, "The Activities of Sākṣi-gopāla" },
            { 6, "The Liberation of Sārvabhauma Bhaṭṭācārya" },
            { 7, "The Lord Begins His Tour of South India" },
            { 8, "Talks Between Śrī Caitanya Mahāprabhu and Rāmānanda Rāya" },
            { 9, "Lord Śrī Caitanya Mahāprabhu's Travels to the Holy Places" },
            { 10, "The Lord's Return to Jagannātha Purī" },
            { 11, "The Beḍā-kīrtana Pastimes of Śrī Caitanya Mahāprabhu" },
            { 12, "The Cleansing of the Guṇḍicā Temple" },
            { 13, "The Ecstatic Dancing of the Lord at Ratha-yātrā" },
            { 14, "Performance of the Vṛndāvana Pastimes" },
            { 15, "The Lord Accepts Prasādam at the House of Sārvabhauma Bhaṭṭācārya" },
            { 16, "The Lord's Attempt to Go to Vṛndāvana" },
            { 17, "The Lord Travels to Vṛndāvana" },
            { 18, "Lord Śrī Caitanya Mahāprabhu's Visit to Śrī Vṛndāvana" },
            { 19, "Lord Śrī Caitanya Mahāprabhu Instructs Śrīla Rūpa Gosvāmī" },
            { 20, "Lord Śrī Caitanya Mahāprabhu Instructs Sanātana Gosvāmī in the Science of the Absolute Truth" },
            { 21, "The Opulence and Sweetness of Lord Śrī Kṛṣṇa" },
            { 22, "The Process of Devotional Service" },
            { 23, "Life's Ultimate Goal-Love of Godhead" },
            { 24, "The Sixty-One Explanations of the Ātmārāma Verse" },
            { 25, "How All the Residents of Vārāṇasī Became Vaiṣṇavas" }
        };

        public static readonly Dictionary<int, string> CcAntya = new()
        {
            { 1, "Śrīla Rūpa Gosvāmī's Second Meeting With the Lord" },
            { 2, "The Chastisement of Junior Haridāsa" },
            { 3, "The Glories of Śrīla Haridāsa Ṭhākura" },
            { 4, "Sanātana Gosvāmī Visits the Lord at Jagannātha Purī" },
            { 5, "How Pradyumna Miśra Received Instructions from Rāmānanda Rāya" },
            { 6, "The Meeting of Śrī Caitanya Mahāprabhu and Raghunatha dasa Gosvāmī" },
            { 7, "The Meeting of Śrī Caitanya Mahāprabhu and Vallabha Bhaṭṭa" },
            { 8, "Rāmacandra Purī Criticizes the Lord" },
            { 9, "The Deliverance of Gopīnātha Paṭṭanāyaka" },
            { 10, "Śrī Caitanya Mahāprabhu Accepts Prasādam from His Devotees" },
            { 11, "The Passing of Haridāsa Ṭhākura" },
            { 12, "The Loving Dealings Between Lord Śrī Caitanya Mahāprabhu and Jagadānanda Paṇḍita" },
            { 13, "Pastimes with Jagadānanda Paṇḍita and Raghunātha Bhaṭṭa Gosvāmī" },
            { 14, "Lord Śrī Caitanya Mahāprabhu's Feelings of Separation from Kṛṣṇa" },
            { 15, "The Transcendental Madness of Lord Śrī Caitanya Mahāprabhu" },
            { 16, "Lord Śrī Caitanya Mahāprabhu Tastes Nectar from the Lips of Lord Śrī Kṛṣṇa" },
            { 17, "The Bodily Transformations of Lord Śrī Caitanya Mahāprabhu" },
            { 18, "Rescuing the Lord from the Sea" },
            { 19, "The Inconceivable Behavior of Lord Śrī Caitanya Mahāprabhu" },
            { 20, "The Śikṣāṣṭaka Prayers" }
        };

        public static string? GetBgTitle(int chapter) => Bg.TryGetValue(chapter, out var t) ? t : null;

        public static string? GetSbTitle(int canto, int chapter) => Sb.TryGetValue((canto, chapter), out var t) ? t : null;

        public static string? GetCcTitle(string bookKey, int chapter)
        {
            var dict = bookKey switch
            {
                "DI" => CcAdi,
                "MADHYA" => CcMadhya,
                "ANTYA" => CcAntya,
                _ => null
            };
            return dict != null && dict.TryGetValue(chapter, out var t) ? t : null;
        }
    }
}
