Name:           mono-webserver-hyperfastcgi
Url:            https://github.com/xplicit/HyperFastCgi
License:        X11/MIT
Group:          Productivity/Networking/Web/Servers
Version:        0.4
Release:        1
Summary:        Mono WebServer HyperFastCgi
Source:         %{name}-%{version}-%{release}.tar
BuildRoot:      %{_tmppath}/%{name}-%{version}-build
BuildArch:      noarch
#BuildRequires:  mono-devel

# To build the tar, you can use : 
# git archive --format=tar --prefix=mono-webserver-hyperfastcgi-0.4-1/ master > ~/rpmbuild/SOURCES/mono-webserver-hyperfastcgi-0.4-1.tar

%description
Performant nginx to mono fastcgi server

%prep
%setup -q -n %{name}-%{version}-%{release}

%build
sed -i 's#"1.0.*"#"4.0.*"#' src/HyperFastCgi/Properties/AssemblyInfo.cs
xbuild /tv:4.0 /p:Configuration=Release /target:rebuild /p:SignAssembly=true /p:AssemblyOriginatorKeyFile="../mono.snk"
gacutil -i src/HyperFastCgi/bin/Release/HyperFastCgi.exe -package 4.0 -gacdir .%{_prefix}

%install
rm -rf %{buildroot}/*
mv .%{_prefix} %{buildroot}%{_prefix}
mkdir -p %{buildroot}%{_bindir}
echo "#!/bin/sh" > %{buildroot}%{_bindir}/hyperfastcgi4
echo 'exec %{_bindir}/mono $MONO_OPTIONS "%{_prefix}/lib/mono/gac/HyperFastCgi/0.4.4.0__0738eb9f132ed756/HyperFastCgi.exe" "$@"' >> %{buildroot}%{_bindir}/hyperfastcgi4
chmod +x %{buildroot}%{_bindir}/hyperfastcgi4

%clean
rm -rf %{buildroot}

%files
%defattr(-,root,root)
%{_bindir}/hyperfastcgi4
%{_prefix}/lib/mono/4.0/HyperFastCgi.exe
%{_prefix}/lib/mono/gac/HyperFastCgi/*

%changelog
