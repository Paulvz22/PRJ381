namespace BCOpendayApp.Services;

public sealed record CampusStop(string Id, string Name, string Image, string Summary, string Details, string Highlight);

// Local content keeps the visitor experience available without a server.
public static class CampusCatalog
{
    public static IReadOnlyList<CampusStop> Stops { get; } = new List<CampusStop>
    {
        new("robotics", "Robotics Lab", "roboticslab.png", "Discover automation and intelligent machines.", "Explore how sensors, software and robotic systems work together. Ask your tour guide about practical projects and safe demonstrations. Do not touch equipment without permission.", "Industrial robot arm • Sensors • Automation"),
        new("manufacturing", "Manufacturing Lab", "manufacturinglab.png", "See ideas become practical prototypes.", "Discover the connection between design, technology and manufacturing. During your visit, ask how students develop and test prototypes. Follow staff instructions around machinery.", "Design • Prototyping • Precision"),
        new("lecture", "Lecture Hall", "lecturehall.png", "Step inside a collaborative learning space.", "Explore the spaces where students discuss concepts, solve problems and present their work. Ask your guide about the student learning experience and classroom support.", "Learning • Collaboration • Presentations"),
        new("library", "Library", "library.png", "Find your space to study and research.", "Discover the study environment and ask staff about learning resources. Keep noise low, respect students who are studying and ask before taking photographs.", "Research • Reading • Quiet study"),
        new("lounge", "Student Lounge", "studentlounge.png", "Get a feel for life beyond the classroom.", "Explore a place to meet fellow students and take a break. Ask your guide about clubs, student leadership and campus activities that interest you.", "Community • Clubs • Student life")
    };
    public static CampusStop? Find(string id) => Stops.FirstOrDefault(s => s.Id == id);
}
