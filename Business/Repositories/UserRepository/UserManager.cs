﻿using Business.Abstract;
using Business.Aspects.Secured;
using Business.Repositories.EmailParameterRepository;
using Business.Repositories.UserRepository.Contans;
using Business.Repositories.UserRepository.Validation;
using Core.Aspects.Caching;
using Core.Aspects.Performance;
using Core.Aspects.Validation;
using Core.Utilities.Business;
using Core.Utilities.Hashing;
using Core.Utilities.Result.Abstract;
using Core.Utilities.Result.Concrete;
using DataAccess.Repositories.UserOperationClaimRepository;
using DataAccess.Repositories.UserRepository;
using Entities.Concrete;
using Entities.Dtos;

namespace Business.Repositories.UserRepository
{
    public class UserManager : IUserService
    {
        private readonly IUserDal _userDal;
        private readonly IFileService _fileService;
        private readonly IEmailParameterService _emailParameterService;
        private readonly IUserOperationClaimDal _userOperationClaimDal;

        public UserManager(IUserDal userDal, IFileService fileService, IEmailParameterService emailParameterService, IUserOperationClaimDal userOperationClaimDal = null)
        {
            _userDal = userDal;
            _fileService = fileService;
            _emailParameterService = emailParameterService;
            _userOperationClaimDal = userOperationClaimDal;
        }


        // Kullanıcıyı Kaydet
        [RemoveCacheAspect("IUserService.Get")]
        public async Task Add(RegisterAuthDto registerDto)
        {
            string confirmValue = await CreateConfirmValue();

            var user = CreateUser(registerDto);

            user.ConfirmValue = confirmValue;

            await _userDal.Add(user);

            //await SendConfirmUserMail(user.Email);
        }
        //****************************************//

        // Kullanıcı Oluşturulurken Hesap Doğrulama Kodu Oluştur
        public async Task<string> CreateConfirmValue()
        {
        again:;
            string value = Guid.NewGuid().ToString();
            var result = await _userDal.Get(p => p.ConfirmValue == value);
            if (result != null)
            {
                goto again;
            }

            return value;
        }
        //****************************************//

        // Kullanıcıyı Oluştur
        private static User CreateUser(RegisterAuthDto registerDto)
        {
            byte[] passwordHash, paswordSalt;
            HashingHelper.CreatePassword(registerDto.Password, out passwordHash, out paswordSalt);

            User user = new();
            user.Id = 0;
            user.Email = registerDto.Email;
            user.Name = registerDto.Name;
            user.PasswordHash = passwordHash;
            user.PasswordSalt = paswordSalt;
            return user;
        }
        //****************************************//

        // Kullanıcıyı Mail Adresine Göre Bul
        public async Task<User> GetByEmail(string email)
        {
            var result = await _userDal.Get(p => p.Email == email);
            return result;
        }
        //****************************************//

        // Kullanıcıyı Güncelle
        [SecuredAspect("Admin,Kullanıcı")]
        [ValidationAspect(typeof(UserValidator))]
        [RemoveCacheAspect("IUserService.Get")]
        public async Task<IResult> Update(User user)
        {
            await _userDal.Update(user);
            return new SuccessResult(UserMessages.UpdatedUser);
        }
        //****************************************//

        // Kullanıcıyı Sil
        [SecuredAspect("Admin")]
        [RemoveCacheAspect("IUserService.Get")]
        public async Task<IResult> Delete(User user)
        {
            IResult result = BusinessRules.Run(
               await CheckIfUserExistToUserClaims(user.Id)
               );
            if (result != null)
            {
                return result;
            }

            await _userDal.Delete(user);
            return new SuccessResult(UserMessages.DeletedUser);
        }
        //****************************************//

        // Kullanıcıları Listele
        [SecuredAspect("Admin,Müşteri")]
        [CacheAspect()]
        [PerformanceAspect()]
        public async Task<IDataResult<List<User>>> GetList()
        {
            return new SuccessDataResult<List<User>>(await _userDal.GetAll());
        }
        //****************************************//

        // Kullanıcıyı Id'ye Göre Getir
        public async Task<IDataResult<User>> GetById(int id)
        {
            return new SuccessDataResult<User>(await _userDal.Get(p => p.Id == id));
        }
        //****************************************//

        // Kimlik Doğrulama İçin Kullanıcıyı Tut
        public async Task<User> GetByIdForAuth(int id)
        {
            return await _userDal.Get(p => p.Id == id);
        }
        //****************************************//

        // Kullanıcının Şifresini Değiştir
        [SecuredAspect()]
        [ValidationAspect(typeof(UserChangePasswordValidator))]
        public async Task<IResult> ChangePassword(UserChangePasswordDto userChangePasswordDto)
        {
            var user = await _userDal.Get(p => p.Id == userChangePasswordDto.UserId);
            bool result = HashingHelper.VerifyPasswordHash(userChangePasswordDto.CurrentPassword, user.PasswordHash, user.PasswordSalt);
            if (!result)
            {
                return new ErrorResult(UserMessages.WrongCurrentPassword);
            }

            byte[] passwordHash, paswordSalt;
            HashingHelper.CreatePassword(userChangePasswordDto.NewPassword, out passwordHash, out paswordSalt);

            user.PasswordHash = passwordHash;
            user.PasswordSalt = paswordSalt;
            await _userDal.Update(user);
            return new SuccessResult(UserMessages.PasswordChanged);
        }
        //****************************************//

        // Kullanıcı İçin Şifre Oluştur
        public async Task<IResult> CreateANewPassword(CreateANewPasswordDto createANewPasswordDto)
        {
            var user = await _userDal.Get(p => p.ForgotPasswordValue == createANewPasswordDto.ForgotPasswordValue);

            if (user == null)
                return new ErrorResult(UserMessages.ForgotPasswordValueIsNotValid);

            var result = BusinessRules.Run(
                IsForgotPasswordValueUsed(user),
                IsForgotPasswordDateEnded(user)
                );
            if (result != null)
                return result;

            byte[] passwordHash, paswordSalt;
            HashingHelper.CreatePassword(createANewPasswordDto.NewPassword, out passwordHash, out paswordSalt);

            user.PasswordHash = passwordHash;
            user.PasswordSalt = paswordSalt;
            await _userDal.Update(user);
            return new SuccessResult(UserMessages.PasswordChanged);
        }
        //****************************************//

        // Kullanıcının Parola Sıfırlamayı Önceden Kullanıldığını Kontrol Eder
        public static IResult IsForgotPasswordValueUsed(User user)
        {
            if (user.IsForgotPasswordComplete)
                return new ErrorResult(UserMessages.ForgotPasswordValueIsUsed);
            else
                return new SuccessResult();
        }
        //****************************************//

        // Kullanıcının Şifre Sıfırlama İsteğinin Geçerli Olduğu Süre
        public static IResult IsForgotPasswordDateEnded(User user)
        {
            DateTime date1 = DateTime.Now;
            DateTime date2 = Convert.ToDateTime(user.ForgotPasswordRequestDate);
            TimeSpan result = date2 - date1;
            var remainMin = Convert.ToInt16(result.Minutes.ToString());
            if (remainMin < -10)
            {
                return new ErrorResult(UserMessages.ForgotPasswordValueTimeIsEnded);
            }

            return new SuccessResult();
        }
        //****************************************//

        // Kullanıcının Sahip Olduğu İzinleri Listele
        public async Task<List<OperationClaim>> GetUserOperationClaims(int userId)
        {
            return await _userDal.GetUserOperatinonClaims(userId);
        }
        //****************************************//

        // Kullanıcı Doğrulama İşlemi
        public async Task<IResult> ConfirmUser(string confirmValue)
        {
            var user = await _userDal.Get(p => p.ConfirmValue == confirmValue);
            if (user.IsConfirm)
            {
                return new ErrorResult(UserMessages.UserAlreadyConfirm);
            }

            user.IsConfirm = true;
            await _userDal.Update(user);
            return new SuccessResult(UserMessages.UserConfirmIsSuccesiful);
        }
        //****************************************//

        // Kullanıcının Şifremi Unuttum Maili Gönderme
        public async Task<IResult> SendForgotPasswordMail(string email)
        {
            var user = await _userDal.Get(p => p.Email == email);
            var result = BusinessRules.Run(CheckForgotPasswordIsRequestActive(user));
            if (result != null)
            {
                return result;
            }

            string forgotPasswordValue = await CreateForgotPasswordValue();


            var emailParameter = await _emailParameterService.GetFirst();
            if (emailParameter != null)
            {
                user.ForgotPasswordValue = forgotPasswordValue;
                user.ForgotPasswordRequestDate = DateTime.Now;
                user.IsForgotPasswordComplete = false;
                await _userDal.Update(user);


                string subject = "Şifre Hatırlatma Maili";
                string body = ForgotPasswordEmailHtmlBody(forgotPasswordValue); ;
                await _emailParameterService.SendEmail(emailParameter, body, subject, email);
            }

            return new SuccessResult(UserMessages.ForgotPasswordMailSendSuccessiful);
        }
        //****************************************//

        // Kullanıcının Şifremi Unuttum İsteğinin Aktif Olup Olmadığını Kontrol Eder
        public IResult CheckForgotPasswordIsRequestActive(User user)
        {
            if (!user.IsForgotPasswordComplete)
            {
                if (user.ForgotPasswordRequestDate != null)
                {
                    DateTime date1 = DateTime.Now;
                    DateTime date2 = Convert.ToDateTime(user.ForgotPasswordRequestDate);
                    TimeSpan result = date2 - date1;
                    var remainMin = Convert.ToInt16(result.Minutes.ToString());
                    if (remainMin >= -10)
                    {
                        return new ErrorResult(UserMessages.AlreadySendForgotPasswordMail);
                    }
                }
            }

            return new SuccessResult();
        }
        //****************************************//

        // Kullanıcının Şifresini Belirli Sürede Sıfırlamasını Kontrol Eder
        public async Task<string> CreateForgotPasswordValue()
        {
        again:;
            string value = Guid.NewGuid().ToString();
            var result = await _userDal.Get(p => p.ForgotPasswordValue == value);
            if (result != null)
            {
                goto again;
            }

            return value;
        }
        //****************************************//

        // Kullanıcının Şifremi Unuttum Mailinin HTML Olarak Hazırlanışı
        public string ForgotPasswordEmailHtmlBody(string forgotPasswordValue)
        {
            string css = "{text - decoration: underline!important}";
            string body = $"<!doctype html><html lang='en-US'><head><meta content = 'text/html; charset=utf-8' http - equiv = 'Content-Type'/>    <title> Şifre Yenileme İsteği </title><meta name = 'description' content = 'Şifre Yenileme İsteği.'><style type = 'text/css'> a:hover {css}  </style></head><body marginheight = '0' topmargin = '0' marginwidth = '0' style = 'margin: 0px; background-color: #f2f3f8;' leftmargin = '0'><!--100 % body table--><table cellspacing = '0' border = '0' cellpadding = '0' width = '100%' bgcolor = '#f2f3f8' style = '@import url(https://fonts.googleapis.com/css?family=Rubik:300,400,500,700|Open+Sans:300,400,600,700); font-family: 'Open Sans', sans-serif;'><tr><td><table style = 'background-color: #f2f3f8; max-width:670px;  margin:0 auto;' width = '100%' border = '0' align = 'center' cellpadding = '0' cellspacing = '0'><tr><td style = 'height:80px;' > &nbsp;</td></tr>                   <tr><td style = 'text-align:center;'></td></tr><tr> <td style = 'height:20px;' > &nbsp;</td></tr><tr><td><table width = '95%' border = '0' align = 'center' cellpadding = '0' cellspacing = '0' style = 'max-width:670px;background:#fff; border-radius:3px; text-align:center;-webkit-box-shadow:0 6px 18px 0 rgba(0,0,0,.06);-moz-box-shadow:0 6px 18px 0 rgba(0,0,0,.06);box-shadow:0 6px 18px 0 rgba(0,0,0,.06);'><tr><td style = 'height:40px;' > &nbsp;</td></tr><tr><td style = 'padding:0 35px;'><h1 style = 'color:#1e1e2d; font-weight:500; margin:0;font-size:32px;font-family:'Rubik',sans-serif;'> Şifrenizi yenilemek için talepte bulundunuz</h1><span style = 'display:inline-block; vertical-align:middle; margin:29px 0 26px; border-bottom:1px solid #cecece; width:100px;'></span>  <p style = 'color:#455056; font-size:15px;line-height:24px; margin:0;'>Güvenlik sebebiyle eski şifrenizi burada gösteremiyoruz. Yeni şifre oluşturmak için 5 dakika içerisinde aşağıdaki linke tıklayarak açılacak sayfadan yeni şifrenizi belirleyebilirsiniz</p><a href='https://www.sitem.com/pasword/reset/{forgotPasswordValue}' style = 'background:#20e277;text-decoration:none !important; font-weight:500; margin-top:35px; color:#fff;text-transform:uppercase; font-size:14px;padding:10px 24px;display:inline-block;border-radius:50px;'> Şifreyi Sıfırla </a></td></tr><tr><td style = 'height:40px;'> &nbsp;</td></tr></table></td><tr><td style = 'height:20px;'> &nbsp;</td></tr><tr><td style = 'text-align:center;'><p style = 'font-size:14px; color:rgba(69, 80, 86, 0.7411764705882353); line-height:18px; margin:0 0 0;' > </p></td></tr><tr><td style = 'height:80px;'>&nbsp;</td></tr></table></td></tr></table><!--/ 100 % body table--></body></html>";

            return body;
        }
        //****************************************//

        // Kullanıcının Hesabının Doğrulama İşlemi Mail HTML Olarak Hazırlanışı
        public string ConfirmUserHtmlBody(string confirmValue)
        {
            string css = "{text - decoration: underline!important}";
            string body = $"<!doctype html><html lang='en-US'><head><meta content = 'text/html; charset=utf-8' http - equiv = 'Content-Type'/>    <title > Kullanıcı Onaylama </title><meta name = 'description' content = 'Kullanıcı Onaylama.'><style type = 'text/css'> a:hover {css}  </style></head><body marginheight = '0' topmargin = '0' marginwidth = '0' style = 'margin: 0px; background-color: #f2f3f8;' leftmargin = '0'><!--100 % body table--><table cellspacing = '0' border = '0' cellpadding = '0' width = '100%' bgcolor = '#f2f3f8' style = '@import url(https://fonts.googleapis.com/css?family=Rubik:300,400,500,700|Open+Sans:300,400,600,700); font-family: 'Open Sans', sans-serif;'><tr><td><table style = 'background-color: #f2f3f8; max-width:670px;  margin:0 auto;' width = '100%' border = '0' align = 'center' cellpadding = '0' cellspacing = '0'><tr><td style = 'height:80px;' > &nbsp;</td></tr><tr><td style = 'text-align:center;'></td></tr><tr> <td style = 'height:20px;' > &nbsp;</td></tr><tr><td><table width = '95%' border = '0' align = 'center' cellpadding = '0' cellspacing = '0' style = 'max-width:670px;background:#fff; border-radius:3px; text-align:center;-webkit-box-shadow:0 6px 18px 0 rgba(0,0,0,.06);-moz-box-shadow:0 6px 18px 0 rgba(0,0,0,.06);box-shadow:0 6px 18px 0 rgba(0,0,0,.06);'><tr><td style = 'height:40px;'>&nbsp;</td></tr><tr><td style = 'padding:0 35px;'><h1 style = 'color:#1e1e2d; font-weight:500; margin:0;font-size:32px;font-family:'Rubik',sans-serif;'>Kullanıcı Onaylama Maili</h1><span style = 'display:inline-block; vertical-align:middle; margin:29px 0 26px; border-bottom:1px solid #cecece; width:100px;'></span> <p style = 'color:#455056; font-size:15px;line-height:24px; margin:0;'>Kullanıcı kaydınızı doğrulamak için aşağıdaki linke tıklayarak kullanıcı kaydını aktif edebilirsiniz</p><a href='https://www.sitem.com/user/confirm/{confirmValue}' style = 'background:#20e277;text-decoration:none !important; font-weight:500; margin-top:35px; color:#fff;text-transform:uppercase; font-size:14px;padding:10px 24px;display:inline-block;border-radius:50px;'> Kullanıcı Onayla </a></td></tr><tr><td style = 'height:40px;'> &nbsp;</td></tr></table></td><tr><td style = 'height:20px;'> &nbsp;</td></tr><tr><td style = 'text-align:center;'><p style = 'font-size:14px; color:rgba(69, 80, 86, 0.7411764705882353); line-height:18px; margin:0 0 0;' > </p></td></tr><tr><td style = 'height:80px;'>&nbsp;</td></tr></table></td></tr></table><!--/ 100 % body table--></body></html>";

            return body;
        }
        //****************************************//

        // Kullanıcıya Doğrulama Maili Gönderme
        public async Task<IResult> SendConfirmUserMail(string email)
        {
            var user = await _userDal.Get(p => p.Email == email);
            if (user == null)
                return new ErrorResult(UserMessages.UserNotFound);

            if (user.IsConfirm)
                return new ErrorResult(UserMessages.UserAlreadyConfirm);

            var emailParameter = await _emailParameterService.GetFirst();
            if (emailParameter != null)
            {
                string subject = "Kullanıcı Onaylama Maili";
                string body = ConfirmUserHtmlBody(user.ConfirmValue);
                await _emailParameterService.SendEmail(emailParameter, body, subject, email);
            }

            return new SuccessResult(UserMessages.ConfirmUserMailSendSuccessiful);
        }
        //****************************************//

        public async Task<IResult> CheckIfUserExistToUserClaims(int userId)
        {
            var result = await _userOperationClaimDal.Get(x=>x.UserId == userId);
            if (result!=null)
            {
                return new ErrorResult("Silmeye Çalıştığınız Kullanıcının Yetkisi Bulunuyor.!");
            }
            return new SuccessResult();
        }
    }
}
