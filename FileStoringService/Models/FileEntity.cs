using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileStoringService.Models;

public class FileEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }
    
    [Required]
    public string FileName { get; set; }
    
    [Required]
    public byte[] Content { get; set; }
    
    [Required]
    public DateTime UploadDate { get; set; }
    
    [Required]
    public string FileHash { get; set; }
    
    [Required]
    public string FilePath { get; set; }
} 