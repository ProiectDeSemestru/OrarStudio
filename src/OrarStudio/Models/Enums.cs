namespace OrarStudio.Models;

/// <summary>Tipul lecției: curs (prelegere), seminar sau laborator.</summary>
public enum LessonType { Curs, Seminar, Laborator }

/// <summary>Periodicitatea lecției: în fiecare săptămână sau doar în săptămânile impare/pare.</summary>
public enum WeekParity { Saptamanal, Impara, Para }

/// <summary>Categoria auditoriului (unde se țin cursuri, seminare sau laboratoare).</summary>
public enum RoomKind { Amfiteatru, SalaSeminar, LaboratorInformatica, LaboratorSpecializat, Universala }

/// <summary>Limba de instruire.</summary>
public enum TeachingLanguage { RO, RU, EN }

public enum IssueSeverity { Info, Warning, Error }

public enum IssueKind { TeacherConflict, GroupConflict, RoomConflict, RoomCapacity, RoomType, TeacherUnavailable, NoRoom, NoTeacher }

/// <summary>Perspectiva din care este afișat orarul în editor.</summary>
public enum ScheduleViewMode { Group, Teacher, Room }
