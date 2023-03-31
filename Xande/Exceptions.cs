namespace Xande.Havok;

// I don't know if this is a good idea or not, someone please yell at me if so (you can tell I miss Result<> and thiserror) ~jules
public class Exceptions {
    public class HavokFailureException : Exception {
        public HavokFailureException() : base( "Havok returned failure" ) { }
    }

    public class HavokReadException : Exception {
        public HavokReadException() : base( "Havok failed to read resource" ) { }
    }

    public class HavokWriteException : Exception {
        public HavokWriteException() : base( "Havok failed to write resource" ) { }
    }

    public class SklbInvalidException : Exception {
        public SklbInvalidException() : base( ".sklb file is invalid" ) { }
    }
}